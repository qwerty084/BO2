<#
.SYNOPSIS
Reads the Event Monitor shared-memory snapshot for a BO2 process.

.DESCRIPTION
The current v6 snapshot layout is loaded from
contracts/EventMonitorSnapshotContract.v1.json. Legacy fallback event counts are
kept only so this troubleshooting script can still inspect older partial maps
created before the contract fixed the Event Monitor capacity at 128 records.

.EXAMPLE
.\tools\Read-BO2MonitorSharedMem.ps1 -ProcessId 1234

Reads BO2MonitorSharedMem-1234 using the contract-defined v6 layout.

.EXAMPLE
.\tools\Read-BO2MonitorSharedMem.ps1

Discovers the first running t6zm.exe process, then opens its process-scoped map.
#>
param(
    [int]$ProcessId = 0,

    [string]$ContractPath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'contracts\EventMonitorSnapshotContract.v1.json')
)

$ErrorActionPreference = 'Stop'

function Get-RequiredProperty {
    param(
        [object]$Value,

        [Parameter(Mandatory)]
        [string]$Name
    )

    if ($null -eq $Value) {
        throw "Contract is missing required value '$Name'."
    }

    return $Value
}

function Get-ContractField {
    param(
        [Parameter(Mandatory)]
        [object[]]$Fields,

        [Parameter(Mandatory)]
        [string]$Name
    )

    $field = $Fields | Where-Object { $_.name -eq $Name } | Select-Object -First 1
    if ($null -eq $field) {
        throw "Contract is missing required field '$Name'."
    }

    return $field
}

function Read-UInt32Field {
    param(
        [Parameter(Mandatory)]
        [System.IO.MemoryMappedFiles.MemoryMappedViewAccessor]$View,

        [Parameter(Mandatory)]
        [object]$Field
    )

    return $View.ReadUInt32([int64]$Field.offset)
}

function Read-Int32Field {
    param(
        [Parameter(Mandatory)]
        [System.IO.MemoryMappedFiles.MemoryMappedViewAccessor]$View,

        [Parameter(Mandatory)]
        [object]$Field
    )

    return $View.ReadInt32([int64]$Field.offset)
}

function Read-RecordUInt32Field {
    param(
        [Parameter(Mandatory)]
        [System.IO.MemoryMappedFiles.MemoryMappedViewAccessor]$View,

        [Parameter(Mandatory)]
        [int64]$RecordOffset,

        [Parameter(Mandatory)]
        [object]$Field
    )

    return $View.ReadUInt32($RecordOffset + [int64]$Field.offset)
}

function Read-RecordInt32Field {
    param(
        [Parameter(Mandatory)]
        [System.IO.MemoryMappedFiles.MemoryMappedViewAccessor]$View,

        [Parameter(Mandatory)]
        [int64]$RecordOffset,

        [Parameter(Mandatory)]
        [object]$Field
    )

    return $View.ReadInt32($RecordOffset + [int64]$Field.offset)
}

function Read-NullTerminatedUtf8Field {
    param(
        [Parameter(Mandatory)]
        [System.IO.MemoryMappedFiles.MemoryMappedViewAccessor]$View,

        [Parameter(Mandatory)]
        [int64]$RecordOffset,

        [Parameter(Mandatory)]
        [object]$Field
    )

    $bytes = New-Object byte[] ([int]$Field.size)
    [void]$View.ReadArray($RecordOffset + [int64]$Field.offset, $bytes, 0, $bytes.Length)
    $zero = [Array]::IndexOf($bytes, [byte]0)
    if ($zero -lt 0) {
        $zero = $bytes.Length
    }

    return [Text.Encoding]::UTF8.GetString($bytes, 0, $zero)
}

if ($ProcessId -le 0) {
    $process = Get-Process -Name t6zm -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $process) {
        throw 'Pass -ProcessId or start t6zm.exe first.'
    }

    $ProcessId = $process.Id
}

$contract = Get-Content -Path $ContractPath -Raw | ConvertFrom-Json
$snapshot = Get-RequiredProperty $contract.snapshot 'snapshot'
$objectNamePrefixes = Get-RequiredProperty $contract.objectNamePrefixes 'objectNamePrefixes'
$layout = Get-RequiredProperty $contract.layout 'layout'
$headerFields = Get-RequiredProperty $layout.headerFields 'layout.headerFields'
$eventRecordFields = Get-RequiredProperty $layout.eventRecordFields 'layout.eventRecordFields'

$expectedMagic = [Convert]::ToUInt32($snapshot.magicHex.Substring(2), 16)
$maxEventCount = [uint32](Get-RequiredProperty $snapshot.maxEventCount 'snapshot.maxEventCount')
$headerSize = [int64](Get-RequiredProperty $snapshot.headerSize 'snapshot.headerSize')
$eventRecordSize = [int64](Get-RequiredProperty $snapshot.eventRecordSize 'snapshot.eventRecordSize')
$contractSharedMemorySize = [int64](Get-RequiredProperty $snapshot.sharedMemorySize 'snapshot.sharedMemorySize')
$computedSharedMemorySize = $headerSize + ([int64]$maxEventCount * $eventRecordSize)
if ($contractSharedMemorySize -ne $computedSharedMemorySize) {
    throw "Contract sharedMemorySize $contractSharedMemorySize does not match header/event/count size $computedSharedMemorySize."
}

$magicField = Get-ContractField $headerFields 'Magic'
$versionField = Get-ContractField $headerFields 'Version'
$stateField = Get-ContractField $headerFields 'CompatibilityState'
$eventWriteIndexField = Get-ContractField $headerFields 'EventWriteIndex'
$droppedEventCountField = Get-ContractField $headerFields 'DroppedEventCount'
$eventCountField = Get-ContractField $headerFields 'EventCount'
$droppedNotifyCountField = Get-ContractField $headerFields 'DroppedNotifyCount'
$publishedNotifyCountField = Get-ContractField $headerFields 'PublishedNotifyCount'
$writeSequenceField = Get-ContractField $headerFields 'WriteSequence'

$eventTypeField = Get-ContractField $eventRecordFields 'EventType'
$levelTimeField = Get-ContractField $eventRecordFields 'LevelTime'
$ownerIdField = Get-ContractField $eventRecordFields 'OwnerId'
$stringValueField = Get-ContractField $eventRecordFields 'StringValue'
$tickField = Get-ContractField $eventRecordFields 'Tick'
$eventNameField = Get-ContractField $eventRecordFields 'EventName'
$weaponNameField = Get-ContractField $eventRecordFields 'WeaponName'

$sharedMemoryPrefix = Get-RequiredProperty $objectNamePrefixes.sharedMemory 'objectNamePrefixes.sharedMemory'
$sharedMemoryName = "$sharedMemoryPrefix$ProcessId"
$mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting($sharedMemoryName)
$view = $null
try {
    $view = $mmf.CreateViewAccessor(0, $contractSharedMemorySize, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
}
catch {
    # Legacy troubleshooting fallback only: pre-contract diagnostic builds sometimes
    # exposed shorter maps. Current v6 facts still come from the JSON contract.
    $legacyEventCountFallbacks = @(64, 16)
    foreach ($legacyEventCount in $legacyEventCountFallbacks) {
        $legacySharedMemorySize = $headerSize + ([int64]$legacyEventCount * $eventRecordSize)
        try {
            $view = $mmf.CreateViewAccessor(0, $legacySharedMemorySize, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
            $maxEventCount = [uint32]$legacyEventCount
            break
        }
        catch {
            if ($legacyEventCount -eq $legacyEventCountFallbacks[-1]) {
                throw
            }
        }
    }
}

try {
    $magic = Read-UInt32Field $view $magicField
    $version = Read-UInt32Field $view $versionField
    $state = Read-Int32Field $view $stateField
    $eventWriteIndex = Read-UInt32Field $view $eventWriteIndexField
    $dropped = Read-UInt32Field $view $droppedEventCountField
    $eventCount = Read-UInt32Field $view $eventCountField
    $droppedNotifies = Read-UInt32Field $view $droppedNotifyCountField
    $publishedNotifies = Read-UInt32Field $view $publishedNotifyCountField
    $writeSequence = Read-UInt32Field $view $writeSequenceField

    "magic=0x{0:X8} expectedMagic=0x{1:X8} version={2} state={3} writeIndex={4} summaryDropped={5} notifyDropped={6} notifyPublished={7} writeSeq={8} events={9}" -f $magic, $expectedMagic, $version, $state, $eventWriteIndex, $dropped, $droppedNotifies, $publishedNotifies, $writeSequence, $eventCount

    $startSlot = if ($eventCount -ge $maxEventCount) { $eventWriteIndex % $maxEventCount } else { 0 }
    for ($index = 0; $index -lt $eventCount -and $index -lt $maxEventCount; $index++) {
        $slot = ($startSlot + $index) % $maxEventCount
        $offset = $headerSize + ([int64]$slot * $eventRecordSize)
        $eventType = Read-RecordInt32Field $view $offset $eventTypeField
        $levelTime = Read-RecordInt32Field $view $offset $levelTimeField
        $ownerId = Read-RecordUInt32Field $view $offset $ownerIdField
        $stringValue = Read-RecordUInt32Field $view $offset $stringValueField
        $tick = Read-RecordUInt32Field $view $offset $tickField
        $name = Read-NullTerminatedUtf8Field $view $offset $eventNameField
        $weaponName = Read-NullTerminatedUtf8Field $view $offset $weaponNameField

        "event[{0}] type={1} value={2} owner={3} id={4} tick={5} name={6} weapon={7}" -f $index, $eventType, $levelTime, $ownerId, $stringValue, $tick, $name, $weaponName
    }
}
finally {
    if ($null -ne $view) {
        $view.Dispose()
    }
    $mmf.Dispose()
}
