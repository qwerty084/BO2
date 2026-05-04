[CmdletBinding()]
param(
    [switch]$Check
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$schemaPath = Join-Path $repoRoot 'contracts\EventMonitorSnapshotContract.v1.json'
$nativePath = Join-Path $repoRoot 'BO2Monitor\Generated\EventMonitorSnapshotContract.g.h'
$managedPath = Join-Path $repoRoot 'Services\Generated\EventMonitorSnapshotContract.g.cs'
$newLine = "`r`n"
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

function Get-RequiredString($value, [string]$name) {
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        throw "Schema value '$name' must be a non-empty string."
    }

    return $value
}

function Get-RequiredUInt32($value, [string]$name) {
    if ($null -eq $value) {
        throw "Schema value '$name' is required."
    }

    $converted = [uint64]$value
    if ($converted -gt [uint32]::MaxValue) {
        throw "Schema value '$name' must fit in uint32."
    }

    return [uint32]$converted
}

function Get-RequiredInt32($value, [string]$name) {
    if ($null -eq $value) {
        throw "Schema value '$name' is required."
    }

    $converted = [int64]$value
    if ($converted -lt [int32]::MinValue -or $converted -gt [int32]::MaxValue) {
        throw "Schema value '$name' must fit in int32."
    }

    return [int32]$converted
}

function Get-EscapedCppWideString([string]$value) {
    return $value.Replace('\', '\\').Replace('"', '\"')
}

function Get-EscapedCSharpString([string]$value) {
    return $value.Replace('\', '\\').Replace('"', '\"')
}

function Get-GeneratedText([string[]]$lines) {
    return ($lines -join $newLine) + $newLine
}

function Test-GeneratedFile([string]$path, [string]$expected) {
    if (-not (Test-Path -LiteralPath $path)) {
        Write-Error "Generated file is missing: $path"
        return $false
    }

    $actual = [System.IO.File]::ReadAllText($path)
    if ($actual -ne $expected) {
        Write-Error "Generated file is stale: $path"
        return $false
    }

    return $true
}

function Get-FieldConstantPrefix([string]$typeName, [string]$fieldName) {
    return "$typeName$fieldName"
}

function Test-FieldLayout($fields, [uint32]$expectedSize, [string]$layoutName) {
    $cursor = [uint32]0
    $fieldNames = [System.Collections.Generic.HashSet[string]]::new()

    foreach ($field in @($fields)) {
        $fieldName = Get-RequiredString $field.name "$layoutName.name"
        if (-not $fieldNames.Add($fieldName)) {
            throw "Schema layout '$layoutName' contains duplicate field '$fieldName'."
        }

        [void](Get-RequiredString $field.type "$layoutName.$fieldName.type")
        $offset = Get-RequiredUInt32 $field.offset "$layoutName.$fieldName.offset"
        $size = Get-RequiredUInt32 $field.size "$layoutName.$fieldName.size"
        if ($offset -ne $cursor) {
            throw "Schema layout '$layoutName' field '$fieldName' has offset $offset but expected $cursor from field order."
        }

        $cursor += $size
    }

    if ($cursor -ne $expectedSize) {
        throw "Schema layout '$layoutName' size is $cursor but expected $expectedSize."
    }
}

function Get-EnumEntries($entries, [string]$schemaName) {
    $result = @()
    $names = [System.Collections.Generic.HashSet[string]]::new()
    $values = [System.Collections.Generic.HashSet[int]]::new()

    foreach ($entry in @($entries)) {
        $name = Get-RequiredString $entry.name "$schemaName.name"
        $value = Get-RequiredInt32 $entry.value "$schemaName.$name.value"
        if (-not $names.Add($name)) {
            throw "Schema enum '$schemaName' contains duplicate name '$name'."
        }

        if (-not $values.Add($value)) {
            throw "Schema enum '$schemaName' contains duplicate value '$value'."
        }

        $result += [pscustomobject]@{
            Name = $name
            Value = $value
        }
    }

    if ($result.Count -eq 0) {
        throw "Schema enum '$schemaName' must contain at least one value."
    }

    return $result
}

function Add-FieldConstants(
    [System.Collections.Generic.List[string]]$lines,
    [string]$typeName,
    $fields,
    [string]$language) {
    foreach ($field in @($fields)) {
        $fieldName = Get-RequiredString $field.name "$typeName.name"
        $prefix = Get-FieldConstantPrefix $typeName $fieldName
        $offset = Get-RequiredUInt32 $field.offset "$typeName.$fieldName.offset"
        $size = Get-RequiredUInt32 $field.size "$typeName.$fieldName.size"

        if ($language -eq 'cpp') {
            [void]$lines.Add("    constexpr std::size_t $($prefix)Offset = static_cast<std::size_t>($offset);")
            [void]$lines.Add("    constexpr std::size_t $($prefix)Size = static_cast<std::size_t>($size);")
        }
        else {
            [void]$lines.Add("        internal const int $($prefix)Offset = $offset;")
            [void]$lines.Add("        internal const int $($prefix)Size = $size;")
        }
    }
}

function Add-NativeLayoutAssertions(
    [System.Collections.Generic.List[string]]$lines,
    [string]$templateType,
    [string]$contractType,
    $fields) {
    foreach ($field in @($fields)) {
        $fieldName = Get-RequiredString $field.name "$contractType.name"
        $prefix = Get-FieldConstantPrefix $contractType $fieldName
        [void]$lines.Add("        static_assert(offsetof($templateType, $fieldName) == $($prefix)Offset);")
        [void]$lines.Add("        static_assert(sizeof((($templateType*)nullptr)->$fieldName) == $($prefix)Size);")
    }
}

function Get-NativeSupportedVersionExpression([uint32[]]$versions) {
    return (($versions | ForEach-Object { "version == $($_)u" }) -join ' || ')
}

function Get-ManagedSupportedVersionExpression([uint32[]]$versions) {
    return (($versions | ForEach-Object { "version == $($_)u" }) -join ' || ')
}

$contract = Get-Content -LiteralPath $schemaPath -Raw | ConvertFrom-Json

if ($contract.schemaVersion -ne 1) {
    throw "Unsupported Event Monitor Snapshot Contract schemaVersion '$($contract.schemaVersion)'."
}

$magicHex = Get-RequiredString $contract.snapshot.magicHex 'snapshot.magicHex'
if ($magicHex -notmatch '^0x[0-9A-Fa-f]{8}$') {
    throw "Schema value 'snapshot.magicHex' must be an 8-digit hexadecimal value with 0x prefix."
}

$snapshotMagic = [Convert]::ToUInt32($magicHex.Substring(2), 16)
$snapshotVersion = Get-RequiredUInt32 $contract.snapshot.version 'snapshot.version'
$supportedVersions = @($contract.snapshot.supportedVersions | ForEach-Object {
    Get-RequiredUInt32 $_ 'snapshot.supportedVersions'
})
if ($supportedVersions.Count -eq 0) {
    throw "Schema value 'snapshot.supportedVersions' must contain at least one version."
}

if ($supportedVersions -notcontains $snapshotVersion) {
    throw "Schema value 'snapshot.supportedVersions' must include snapshot.version."
}

$maxEventCount = Get-RequiredUInt32 $contract.snapshot.maxEventCount 'snapshot.maxEventCount'
$maxEventNameBytes = Get-RequiredUInt32 $contract.snapshot.maxEventNameBytes 'snapshot.maxEventNameBytes'
$maxWeaponNameBytes = Get-RequiredUInt32 $contract.snapshot.maxWeaponNameBytes 'snapshot.maxWeaponNameBytes'
$headerSize = Get-RequiredUInt32 $contract.snapshot.headerSize 'snapshot.headerSize'
$eventRecordSize = Get-RequiredUInt32 $contract.snapshot.eventRecordSize 'snapshot.eventRecordSize'
$sharedMemorySize = Get-RequiredUInt32 $contract.snapshot.sharedMemorySize 'snapshot.sharedMemorySize'
$expectedSharedMemorySize = $headerSize + ($maxEventCount * $eventRecordSize)
if ($sharedMemorySize -ne $expectedSharedMemorySize) {
    throw "Schema value 'snapshot.sharedMemorySize' is $sharedMemorySize but expected $expectedSharedMemorySize."
}

$headerFields = @($contract.layout.headerFields)
$eventRecordFields = @($contract.layout.eventRecordFields)
Test-FieldLayout $headerFields $headerSize 'layout.headerFields'
Test-FieldLayout $eventRecordFields $eventRecordSize 'layout.eventRecordFields'

$eventNameField = $eventRecordFields | Where-Object { $_.name -eq 'EventName' } | Select-Object -First 1
if ($null -eq $eventNameField -or (Get-RequiredUInt32 $eventNameField.size 'layout.eventRecordFields.EventName.size') -ne $maxEventNameBytes) {
    throw "Schema value 'snapshot.maxEventNameBytes' must match EventName field size."
}

$weaponNameField = $eventRecordFields | Where-Object { $_.name -eq 'WeaponName' } | Select-Object -First 1
if ($null -eq $weaponNameField -or (Get-RequiredUInt32 $weaponNameField.size 'layout.eventRecordFields.WeaponName.size') -ne $maxWeaponNameBytes) {
    throw "Schema value 'snapshot.maxWeaponNameBytes' must match WeaponName field size."
}

$compatibilityStates = Get-EnumEntries $contract.enums.gameCompatibilityState 'enums.gameCompatibilityState'
$eventTypes = Get-EnumEntries $contract.enums.gameEventType 'enums.gameEventType'
$minimumSupportedVersion = ($supportedVersions | Measure-Object -Minimum).Minimum
$maximumSupportedVersion = ($supportedVersions | Measure-Object -Maximum).Maximum
$magicLiteral = ('0x{0:X8}u' -f $snapshotMagic)
$versionLiteral = ('{0}u' -f $snapshotVersion)
$sharedMemoryPrefix = Get-RequiredString $contract.objectNamePrefixes.sharedMemory 'objectNamePrefixes.sharedMemory'
$updateEventPrefix = Get-RequiredString $contract.objectNamePrefixes.updateEvent 'objectNamePrefixes.updateEvent'
$stopEventPrefix = Get-RequiredString $contract.objectNamePrefixes.stopEvent 'objectNamePrefixes.stopEvent'
$schemaRelativePath = 'contracts/EventMonitorSnapshotContract.v1.json'
$generatorRelativePath = 'tools/Generate-EventMonitorSnapshotContract.ps1'

$nativeLines = [System.Collections.Generic.List[string]]::new()
foreach ($line in @(
    '#pragma once',
    '',
    '// <auto-generated />',
    "// Generated by $generatorRelativePath from $schemaRelativePath.",
    '',
    '#include <cstddef>',
    '#include <cstdint>',
    '',
    'namespace BO2Monitor::Generated',
    '{',
    "    constexpr std::uint32_t SnapshotMagic = $magicLiteral;",
    "    constexpr std::uint32_t SnapshotVersion = $versionLiteral;",
    "    constexpr std::uint32_t MinimumSupportedSnapshotVersion = $($minimumSupportedVersion)u;",
    "    constexpr std::uint32_t MaximumSupportedSnapshotVersion = $($maximumSupportedVersion)u;",
    ('    constexpr wchar_t SharedMemoryNamePrefix[] = L"{0}";' -f (Get-EscapedCppWideString $sharedMemoryPrefix)),
    ('    constexpr wchar_t UpdateEventNamePrefix[] = L"{0}";' -f (Get-EscapedCppWideString $updateEventPrefix)),
    ('    constexpr wchar_t StopEventNamePrefix[] = L"{0}";' -f (Get-EscapedCppWideString $stopEventPrefix)),
    "    constexpr std::size_t MaxEventCount = static_cast<std::size_t>($maxEventCount);",
    "    constexpr std::size_t MaxEventNameBytes = static_cast<std::size_t>($maxEventNameBytes);",
    "    constexpr std::size_t MaxWeaponNameBytes = static_cast<std::size_t>($maxWeaponNameBytes);",
    "    constexpr std::size_t HeaderSize = static_cast<std::size_t>($headerSize);",
    "    constexpr std::size_t EventRecordSize = static_cast<std::size_t>($eventRecordSize);",
    "    constexpr std::size_t SharedMemorySize = static_cast<std::size_t>($sharedMemorySize);",
    '')) {
    [void]$nativeLines.Add($line)
}
Add-FieldConstants $nativeLines 'SharedSnapshot' $headerFields 'cpp'
[void]$nativeLines.Add('    constexpr std::size_t SharedSnapshotEventsOffset = HeaderSize;')
[void]$nativeLines.Add('    constexpr std::size_t SharedSnapshotEventsSize = MaxEventCount * EventRecordSize;')
Add-FieldConstants $nativeLines 'GameEventRecord' $eventRecordFields 'cpp'
[void]$nativeLines.Add('')
foreach ($entry in $compatibilityStates) {
    [void]$nativeLines.Add("    constexpr std::int32_t GameCompatibilityState$($entry.Name) = $($entry.Value);")
}

[void]$nativeLines.Add('')
foreach ($entry in $eventTypes) {
    [void]$nativeLines.Add("    constexpr std::int32_t GameEventType$($entry.Name) = $($entry.Value);")
}

foreach ($line in @(
    '',
    '    constexpr bool IsSupportedSnapshotVersion(std::uint32_t version)',
    '    {',
    "        return $(Get-NativeSupportedVersionExpression $supportedVersions);",
    '    }',
    '',
    '    constexpr std::size_t GetEventRecordOffset(std::size_t index)',
    '    {',
    '        return HeaderSize + (index * EventRecordSize);',
    '    }',
    '',
    '    template <typename TSnapshot, typename TEventRecord>',
    '    constexpr void AssertSharedSnapshotLayout()',
    '    {')) {
    [void]$nativeLines.Add($line)
}
Add-NativeLayoutAssertions $nativeLines 'TSnapshot' 'SharedSnapshot' $headerFields
[void]$nativeLines.Add('        static_assert(offsetof(TSnapshot, Events) == SharedSnapshotEventsOffset);')
[void]$nativeLines.Add('        static_assert(sizeof(((TSnapshot*)nullptr)->Events) == SharedSnapshotEventsSize);')
Add-NativeLayoutAssertions $nativeLines 'TEventRecord' 'GameEventRecord' $eventRecordFields
foreach ($line in @(
    '        static_assert(sizeof(TEventRecord) == EventRecordSize);',
    '        static_assert(sizeof(TSnapshot) == SharedMemorySize);',
    '    }',
    '}')) {
    [void]$nativeLines.Add($line)
}

$managedLines = [System.Collections.Generic.List[string]]::new()
foreach ($line in @(
    '// <auto-generated />',
    "// Generated by $generatorRelativePath from $schemaRelativePath.",
    '',
    'namespace BO2.Services.Generated',
    '{',
    '    internal static class EventMonitorSnapshotContract',
    '    {',
    "        internal const uint SnapshotMagic = $magicLiteral;",
    "        internal const uint SnapshotVersion = $versionLiteral;",
    "        internal const uint MinimumSupportedSnapshotVersion = $($minimumSupportedVersion)u;",
    "        internal const uint MaximumSupportedSnapshotVersion = $($maximumSupportedVersion)u;",
    ('        internal const string SharedMemoryNamePrefix = "{0}";' -f (Get-EscapedCSharpString $sharedMemoryPrefix)),
    ('        internal const string UpdateEventNamePrefix = "{0}";' -f (Get-EscapedCSharpString $updateEventPrefix)),
    ('        internal const string StopEventNamePrefix = "{0}";' -f (Get-EscapedCSharpString $stopEventPrefix)),
    "        internal const int MaxEventCount = $maxEventCount;",
    "        internal const int MaxEventNameBytes = $maxEventNameBytes;",
    "        internal const int MaxWeaponNameBytes = $maxWeaponNameBytes;",
    "        internal const int HeaderSize = $headerSize;",
    "        internal const int EventRecordSize = $eventRecordSize;",
    "        internal const int SharedMemorySize = $sharedMemorySize;",
    '')) {
    [void]$managedLines.Add($line)
}
Add-FieldConstants $managedLines 'SharedSnapshot' $headerFields 'cs'
[void]$managedLines.Add('        internal const int SharedSnapshotEventsOffset = HeaderSize;')
[void]$managedLines.Add('        internal const int SharedSnapshotEventsSize = MaxEventCount * EventRecordSize;')
Add-FieldConstants $managedLines 'GameEventRecord' $eventRecordFields 'cs'
[void]$managedLines.Add('')
foreach ($entry in $compatibilityStates) {
    [void]$managedLines.Add("        internal const int GameCompatibilityState$($entry.Name) = $($entry.Value);")
}

[void]$managedLines.Add('')
foreach ($entry in $eventTypes) {
    [void]$managedLines.Add("        internal const int GameEventType$($entry.Name) = $($entry.Value);")
}

foreach ($line in @(
    '',
    '        internal static bool IsSupportedSnapshotVersion(uint version)',
    '        {',
    "            return $(Get-ManagedSupportedVersionExpression $supportedVersions);",
    '        }',
    '    }',
    '}')) {
    [void]$managedLines.Add($line)
}

$nativeContent = Get-GeneratedText $nativeLines.ToArray()
$managedContent = Get-GeneratedText $managedLines.ToArray()

if ($Check) {
    $nativeFresh = Test-GeneratedFile $nativePath $nativeContent
    $managedFresh = Test-GeneratedFile $managedPath $managedContent
    if (-not ($nativeFresh -and $managedFresh)) {
        exit 1
    }

    Write-Host 'Event Monitor Snapshot Contract generated files are up to date.'
    exit 0
}

New-Item -ItemType Directory -Force -Path (Split-Path -Parent $nativePath), (Split-Path -Parent $managedPath) | Out-Null
[System.IO.File]::WriteAllText($nativePath, $nativeContent, $utf8NoBom)
[System.IO.File]::WriteAllText($managedPath, $managedContent, $utf8NoBom)
Write-Host 'Event Monitor Snapshot Contract generated files updated.'
