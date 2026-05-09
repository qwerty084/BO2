<#
.SYNOPSIS
Captures script child-variable alias evidence for one paused vm_notify call.

.DESCRIPTION
This is a read-only runtime research helper. Run it while BO2 is paused in a
debugger at vm_notify entry or at that call's return address. Provide the live
vm_notify arguments from x32dbg:

  entry breakpoint at 0x008F31D0 before the prologue:
    inst        = dword ptr [esp+4]
    ownerId     = dword ptr [esp+8]
    stringValue = dword ptr [esp+0C]
    top         = dword ptr [esp+10]

The script opens t6zm.exe with PROCESS_VM_READ | PROCESS_QUERY_INFORMATION and
does not write to the target process. It reads the known script string table,
the per-instance child bucket/table pointers, exact field candidates, and
owner-scoped string/istring child values. Use -OutputPath to append JSONL
records for before/after comparison under ignored local folders such as
`.reverse`. Use -IncludeFieldScan with -FieldNameRegex or -ValueRegex for
read-only narrowing when the notify owner is unknown.

.EXAMPLE
.\tools\Capture-BO2NotifyOwnerAliases.ps1 -Phase before -Inst 0 -OwnerId 1234 -StringValue 7492 -EventName randomization_done -OutputPath .\.reverse\box-alias-capture-2026-05-09.jsonl

.EXAMPLE
.\tools\Capture-BO2NotifyOwnerAliases.ps1 -Phase after -Inst 0 -OwnerId 1234 -StringValue 7429 -EventName user_grabbed_weapon -IncludeBroadScan

.EXAMPLE
.\tools\Capture-BO2NotifyOwnerAliases.ps1 -Phase snapshot -Inst 0 -OwnerId 901 -ValueRegex '^(python_zm|weapon_python_zm)$' -IncludeFieldScan
#>
param(
    [int]$ProcessId = 0,

    [ValidateSet('before', 'after', 'snapshot')]
    [string]$Phase = 'snapshot',

    [Parameter(Mandatory)]
    [int]$Inst,

    [Parameter(Mandatory)]
    [uint32]$OwnerId,

    [uint32]$StringValue = 0,

    [string]$EventName = '',

    [int]$MaxStringId = 65536,

    [int]$MaxChildId = 0x1ffff,

    [int]$MaxHits = 80,

    [switch]$IncludeBroadScan,

    [switch]$IncludeFieldScan,

    [string]$FieldNameRegex = '',

    [string]$ValueRegex = '',

    [string]$OutputPath = ''
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Bo2NotifyOwnerAliasNative
{
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReadProcessMemory(
        IntPtr process,
        IntPtr baseAddress,
        byte[] buffer,
        int size,
        out IntPtr bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool CloseHandle(IntPtr handle);
}
'@

$processVmRead = 0x0010
$processQueryInformation = 0x0400
$processAccess = $processVmRead -bor $processQueryInformation

$scriptStringDataPointer = [uint32]0x02BF83A4
$scriptStringStride = 0x18
$scriptStringTextOffset = 4
$childBucketsPointerSlotBase = [uint32]0x02DEFB00
$childVariablesPointerSlotBase = [uint32]0x02DEFB80
$scriptInstancePointerStride = 0x200
$scriptChildVariableStride = 0x1c
$scriptChildVariableValueOffset = 0x00
$scriptChildVariableNextOffset = 0x08
$scriptChildVariableTypeOffset = 0x0c
$scriptChildVariableNameLoOffset = 0x0d
$scriptChildVariableFlagsOffset = 0x0e
$scriptChildVariableKeyOffset = 0x10
$scriptChildHashMask = 0x1ffff
$scriptStringType = 2
$scriptIStringType = 3

if ($ProcessId -le 0) {
    $process = Get-Process -Name t6zm -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $process) {
        throw 'Pass -ProcessId or start t6zm.exe first.'
    }

    $ProcessId = $process.Id
}

$script:handle = [Bo2NotifyOwnerAliasNative]::OpenProcess($processAccess, $false, $ProcessId)
if ($script:handle -eq [IntPtr]::Zero) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "OpenProcess failed for PID $ProcessId. Win32 error: $errorCode"
}

function Format-Hex32 {
    param($Value)
    '0x{0:X8}' -f ([uint32](([int64]$Value) -band 0xffffffffL))
}

function Read-ProcessBytes {
    param(
        [uint32]$Address,
        [int]$Count,
        [switch]$Quiet
    )

    $buffer = New-Object byte[] $Count
    $bytesRead = [IntPtr]::Zero
    $ok = [Bo2NotifyOwnerAliasNative]::ReadProcessMemory(
        $script:handle,
        [IntPtr]([int64]$Address),
        $buffer,
        $Count,
        [ref]$bytesRead)

    if (-not $ok -or $bytesRead.ToInt64() -ne $Count) {
        if ($Quiet) {
            return $null
        }

        throw "ReadProcessMemory failed at $(Format-Hex32 $Address)"
    }

    return ,$buffer
}

function Read-UInt32 {
    param(
        [uint32]$Address,
        [switch]$Quiet
    )

    $bytes = Read-ProcessBytes -Address $Address -Count 4 -Quiet:$Quiet
    if ($null -eq $bytes) {
        return $null
    }

    [BitConverter]::ToUInt32($bytes, 0)
}

function Get-NullTerminatedAscii {
    param(
        [byte[]]$Buffer,
        [int]$Offset,
        [int]$MaxLength
    )

    if ($Offset -lt 0 -or $Offset -ge $Buffer.Length) {
        return ''
    }

    $limit = [Math]::Min($MaxLength, $Buffer.Length - $Offset)
    $length = 0
    while ($length -lt $limit -and $Buffer[$Offset + $length] -ne 0) {
        $length++
    }

    if ($length -le 0) {
        return ''
    }

    [Text.Encoding]::ASCII.GetString($Buffer, $Offset, $length)
}

function Read-ScriptString {
    param(
        [byte[]]$StringTable,
        [uint32]$StringId
    )

    if ($StringId -eq 0 -or $StringId -ge $MaxStringId) {
        return ''
    }

    $offset = ([int]$StringId * $scriptStringStride) + $scriptStringTextOffset
    Get-NullTerminatedAscii -Buffer $StringTable -Offset $offset -MaxLength 96
}

function Resolve-ScriptStringId {
    param(
        [byte[]]$StringTable,
        [string]$Name
    )

    for ($id = 1; $id -lt $MaxStringId; $id++) {
        $text = Read-ScriptString -StringTable $StringTable -StringId ([uint32]$id)
        if ($text -eq $Name) {
            return [uint32]$id
        }
    }

    return $null
}

function Test-ZombieWeaponAlias {
    param([string]$Value)

    if ([string]::IsNullOrWhiteSpace($Value) -or $Value.Length -le 3 -or $Value.Length -ge 64) {
        return $false
    }

    return $Value -cmatch '^[a-z0-9_]+_zm$'
}

function Get-TypeName {
    param([uint32]$Type)

    $masked = $Type -band 0x7f
    if ($masked -eq $scriptStringType) {
        return 'string'
    }

    if ($masked -eq $scriptIStringType) {
        return 'istring'
    }

    return "type$masked"
}

function Get-InstanceLayout {
    param([int]$LayoutInst)

    $slotOffset = [uint32]($LayoutInst * $scriptInstancePointerStride)
    $bucketSlot = [uint32]($childBucketsPointerSlotBase + $slotOffset)
    $childSlot = [uint32]($childVariablesPointerSlotBase + $slotOffset)
    $bucketBase = Read-UInt32 -Address $bucketSlot -Quiet
    $childBase = Read-UInt32 -Address $childSlot -Quiet

    if ($null -eq $bucketBase -or $null -eq $childBase -or $bucketBase -lt 0x00100000 -or $childBase -lt 0x00100000) {
        return $null
    }

    [pscustomobject]@{
        inst = $LayoutInst
        bucketSlot = Format-Hex32 $bucketSlot
        childSlot = Format-Hex32 $childSlot
        bucketBase = [uint32]$bucketBase
        childBase = [uint32]$childBase
        bucketBaseHex = Format-Hex32 $bucketBase
        childBaseHex = Format-Hex32 $childBase
    }
}

function Read-ChildTable {
    param([pscustomobject]$Layout)

    $count = $MaxChildId + 1
    $byteCount = $count * $scriptChildVariableStride
    Read-ProcessBytes -Address $Layout.childBase -Count $byteCount
}

function Read-ChildEntryFromTable {
    param(
        [byte[]]$ChildTable,
        [uint32]$ChildId
    )

    $offset = [int]($ChildId * $scriptChildVariableStride)
    if ($offset -lt 0 -or ($offset + $scriptChildVariableStride) -gt $ChildTable.Length) {
        return $null
    }

    $value = [BitConverter]::ToUInt32($ChildTable, $offset + $scriptChildVariableValueOffset)
    $next = [BitConverter]::ToUInt32($ChildTable, $offset + $scriptChildVariableNextOffset)
    $type = [uint32]$ChildTable[$offset + $scriptChildVariableTypeOffset]
    $nameLo = [uint32]$ChildTable[$offset + $scriptChildVariableNameLoOffset]
    $flags = [BitConverter]::ToUInt16($ChildTable, $offset + $scriptChildVariableFlagsOffset)
    $key = [BitConverter]::ToUInt32($ChildTable, $offset + $scriptChildVariableKeyOffset)
    $parentId = [uint32](($key -shr 16) -band 0xffff)
    $nameHi = [uint32]($key -band 0xffff)
    $nameId = [uint32](($nameHi -shl 8) -bor $nameLo)

    [pscustomobject]@{
        childId = [uint32]$ChildId
        value = [uint32]$value
        next = [uint32]$next
        type = [uint32]$type
        typeMasked = [uint32]($type -band 0x7f)
        typeName = Get-TypeName $type
        nameLo = [uint32]$nameLo
        flags = [uint32]$flags
        key = [uint32]$key
        keyHex = Format-Hex32 $key
        parentId = [uint32]$parentId
        nameHi = [uint32]$nameHi
        nameId = [uint32]$nameId
    }
}

function Add-StringContext {
    param(
        [pscustomobject]$Entry,
        [byte[]]$StringTable
    )

    $fieldKind = 'raw'
    $fieldStringId = $Entry.nameId
    if ($Entry.nameId -ge 0x10000) {
        $fieldKind = 'field'
        $fieldStringId = [uint32]($Entry.nameId - 0x10000)
    }

    $fieldName = Read-ScriptString -StringTable $StringTable -StringId $fieldStringId
    $valueString = ''
    if ($Entry.typeMasked -eq $scriptStringType -or $Entry.typeMasked -eq $scriptIStringType) {
        $valueString = Read-ScriptString -StringTable $StringTable -StringId $Entry.value
    }

    [pscustomobject]@{
        childId = $Entry.childId
        parentId = $Entry.parentId
        nameId = $Entry.nameId
        fieldKind = $fieldKind
        fieldStringId = $fieldStringId
        fieldName = $fieldName
        value = $Entry.value
        valueString = $valueString
        isWeaponAlias = Test-ZombieWeaponAlias $valueString
        type = $Entry.type
        typeName = $Entry.typeName
        key = $Entry.keyHex
        next = $Entry.next
        flags = $Entry.flags
    }
}

function Find-ScriptVariable {
    param(
        [pscustomobject]$Layout,
        [uint32]$ParentId,
        [uint32]$NameId,
        [byte[]]$StringTable
    )

    $bucketIndex = (($ParentId * 0x65) + $NameId) -band $scriptChildHashMask
    $bucketAddress = [uint32]($Layout.bucketBase + ($bucketIndex * 4))
    $childId = Read-UInt32 -Address $bucketAddress -Quiet
    if ($null -eq $childId -or $childId -eq 0) {
        return $null
    }

    $expectedKey = (($ParentId -band 0xffff) -shl 16) -bor (($NameId -shr 8) -band 0xffff)
    $visited = 0
    $childTable = Read-ChildTable -Layout $Layout
    while ($childId -ne 0 -and $visited -lt 256) {
        $entry = Read-ChildEntryFromTable -ChildTable $childTable -ChildId $childId
        if ($null -eq $entry) {
            return $null
        }

        if ($entry.key -eq $expectedKey -and $entry.nameId -eq $NameId) {
            return Add-StringContext -Entry $entry -StringTable $StringTable
        }

        $childId = $entry.next
        $visited++
    }

    return $null
}

function Get-FieldCandidateIds {
    param(
        [byte[]]$StringTable,
        [string]$Name
    )

    $id = Resolve-ScriptStringId -StringTable $StringTable -Name $Name
    [pscustomobject]@{
        name = $Name
        stringId = $id
        rawNameId = if ($null -ne $id) { [uint32]$id } else { $null }
        fieldNameId = if ($null -ne $id) { [uint32]($id + 0x10000) } else { $null }
    }
}

try {
    $diagnostics = New-Object System.Collections.Generic.List[string]
    $stringDataBase = Read-UInt32 -Address $scriptStringDataPointer
    if ($null -eq $stringDataBase -or $stringDataBase -lt 0x00100000) {
        throw "Script string table pointer at $(Format-Hex32 $scriptStringDataPointer) is not readable or not initialized."
    }

    $stringTable = Read-ProcessBytes -Address $stringDataBase -Count ($MaxStringId * $scriptStringStride)
    $resolvedStringValueText = if ($StringValue -ne 0) {
        Read-ScriptString -StringTable $stringTable -StringId $StringValue
    }
    else {
        ''
    }

    $targetEventIds = foreach ($name in @('randomization_done', 'user_grabbed_weapon')) {
        [pscustomobject]@{
            name = $name
            stringId = Resolve-ScriptStringId -StringTable $stringTable -Name $name
        }
    }

    $fieldIds = foreach ($name in @('weapon_string', 'grab_weapon_name', 'zbarrier')) {
        Get-FieldCandidateIds -StringTable $stringTable -Name $name
    }

    $layout = Get-InstanceLayout -LayoutInst $Inst
    if ($null -eq $layout) {
        throw "No readable child table pointers found for inst $Inst."
    }

    $exactHits = New-Object System.Collections.Generic.List[object]
    foreach ($field in $fieldIds) {
        if ($null -eq $field.stringId) {
            $diagnostics.Add("field '$($field.name)' not present in live script string table")
            continue
        }

        foreach ($candidate in @(
            @{ label = "$($field.name).raw"; id = $field.rawNameId },
            @{ label = "$($field.name).field"; id = $field.fieldNameId }
        )) {
            $hit = Find-ScriptVariable -Layout $layout -ParentId $OwnerId -NameId $candidate.id -StringTable $stringTable
            if ($null -ne $hit) {
                $hit | Add-Member -NotePropertyName label -NotePropertyValue $candidate.label
                $exactHits.Add($hit)
            }
        }
    }

    $childTable = Read-ChildTable -Layout $layout
    $ownerStringFields = New-Object System.Collections.Generic.List[object]
    $ownerAliasHits = New-Object System.Collections.Generic.List[object]
    for ($childId = 1; $childId -le $MaxChildId; $childId++) {
        $entry = Read-ChildEntryFromTable -ChildTable $childTable -ChildId ([uint32]$childId)
        if ($null -eq $entry -or $entry.key -eq 0 -or $entry.parentId -ne $OwnerId) {
            continue
        }

        if ($entry.typeMasked -ne $scriptStringType -and $entry.typeMasked -ne $scriptIStringType) {
            continue
        }

        $withContext = Add-StringContext -Entry $entry -StringTable $stringTable
        if ($ownerStringFields.Count -lt $MaxHits) {
            $ownerStringFields.Add($withContext)
        }

        if ($withContext.isWeaponAlias) {
            $ownerAliasHits.Add($withContext)
        }
    }

    if ($ownerStringFields.Count -eq 0) {
        $diagnostics.Add("no string/istring owner child fields found for owner $OwnerId")
    }

    if ($ownerAliasHits.Count -eq 0) {
        $diagnostics.Add("no owner-scoped _zm alias found for owner $OwnerId")
    }

    $broadAliasHits = New-Object System.Collections.Generic.List[object]
    if ($IncludeBroadScan) {
        for ($childId = 1; $childId -le $MaxChildId; $childId++) {
            $entry = Read-ChildEntryFromTable -ChildTable $childTable -ChildId ([uint32]$childId)
            $isReadableStringEntry = $null -ne $entry `
                -and $entry.key -ne 0 `
                -and ($entry.typeMasked -eq $scriptStringType -or $entry.typeMasked -eq $scriptIStringType)
            if (-not $isReadableStringEntry) {
                continue
            }

            $withContext = Add-StringContext -Entry $entry -StringTable $stringTable
            if (-not $withContext.isWeaponAlias) {
                continue
            }

            $broadAliasHits.Add($withContext)
            if ($broadAliasHits.Count -ge $MaxHits) {
                break
            }
        }

        if ($broadAliasHits.Count -eq 0) {
            $diagnostics.Add('broad scan found no _zm aliases in the selected instance')
        }
    }

    $fieldScanHits = New-Object System.Collections.Generic.List[object]
    if ($IncludeFieldScan -or -not [string]::IsNullOrWhiteSpace($FieldNameRegex) -or -not [string]::IsNullOrWhiteSpace($ValueRegex)) {
        for ($childId = 1; $childId -le $MaxChildId; $childId++) {
            $entry = Read-ChildEntryFromTable -ChildTable $childTable -ChildId ([uint32]$childId)
            $isReadableStringEntry = $null -ne $entry `
                -and $entry.key -ne 0 `
                -and ($entry.typeMasked -eq $scriptStringType -or $entry.typeMasked -eq $scriptIStringType)
            if (-not $isReadableStringEntry) {
                continue
            }

            $withContext = Add-StringContext -Entry $entry -StringTable $stringTable
            $fieldMatches = [string]::IsNullOrWhiteSpace($FieldNameRegex) -or ($withContext.fieldName -match $FieldNameRegex)
            $valueMatches = [string]::IsNullOrWhiteSpace($ValueRegex) -or ($withContext.valueString -match $ValueRegex)
            if (-not $fieldMatches -or -not $valueMatches) {
                continue
            }

            $fieldScanHits.Add($withContext)
            if ($fieldScanHits.Count -ge $MaxHits) {
                break
            }
        }

        if ($fieldScanHits.Count -eq 0) {
            $diagnostics.Add('field/value regex scan found no string or istring child fields')
        }
    }

    $scriptStringDataPointerHex = Format-Hex32 -Value $scriptStringDataPointer
    $stringDataBaseHex = Format-Hex32 -Value $stringDataBase

    $result = [System.Collections.Specialized.OrderedDictionary]::new()
    $result.Add('timestampUtc', [DateTime]::UtcNow.ToString('o'))
    $result.Add('pid', $ProcessId)
    $result.Add('phase', $Phase)
    $result.Add('inst', $Inst)
    $result.Add('ownerId', $OwnerId)
    $result.Add('stringValue', $StringValue)
    $result.Add('eventName', $EventName)
    $result.Add('resolvedStringValueText', $resolvedStringValueText)
    $result.Add('stringDataPointer', $scriptStringDataPointerHex)
    $result.Add('stringDataBase', $stringDataBaseHex)
    $result.Add('targetEventIds', @($targetEventIds))
    $result.Add('fieldIds', @($fieldIds))
    $result.Add('layout', $layout)
    $result.Add('exactHits', $exactHits.ToArray())
    $result.Add('ownerStringFields', $ownerStringFields.ToArray())
    $result.Add('ownerAliasHits', $ownerAliasHits.ToArray())
    $result.Add('broadAliasHits', $broadAliasHits.ToArray())
    $result.Add('fieldScanHits', $fieldScanHits.ToArray())
    $result.Add('diagnostics', $diagnostics.ToArray())

    $json = $result | ConvertTo-Json -Depth 8
    if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
        $resolvedOutputPath = $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($OutputPath)
        $outputDirectory = Split-Path -Parent $resolvedOutputPath
        if (-not [string]::IsNullOrWhiteSpace($outputDirectory) -and -not (Test-Path -LiteralPath $outputDirectory)) {
            New-Item -ItemType Directory -Path $outputDirectory | Out-Null
        }

        $compactJson = $result | ConvertTo-Json -Depth 8 -Compress
        Add-Content -Path $resolvedOutputPath -Value $compactJson
    }

    $json
}
finally {
    if ($script:handle -ne [IntPtr]::Zero) {
        [void][Bo2NotifyOwnerAliasNative]::CloseHandle($script:handle)
    }
}
