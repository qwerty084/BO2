param(
    [int]$ProcessId = 0
)

$ErrorActionPreference = 'Stop'

if ($ProcessId -le 0) {
    $process = Get-Process -Name t6zm -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $process) {
        throw 'Pass -ProcessId or start t6zm.exe first.'
    }

    $ProcessId = $process.Id
}

$maxEventCount = 128
$headerSize = 36
$eventRecordSize = 84
$sharedMemorySize = $headerSize + ($maxEventCount * $eventRecordSize)

$sharedMemoryName = "BO2MonitorSharedMem-$ProcessId"
$mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting($sharedMemoryName)
$view = $null
try {
    $view = $mmf.CreateViewAccessor(0, $sharedMemorySize, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
}
catch {
    $maxEventCount = 64
    $sharedMemorySize = $headerSize + ($maxEventCount * $eventRecordSize)
    try {
        $view = $mmf.CreateViewAccessor(0, $sharedMemorySize, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
    }
    catch {
        $maxEventCount = 16
        $sharedMemorySize = $headerSize + ($maxEventCount * $eventRecordSize)
        $view = $mmf.CreateViewAccessor(0, $sharedMemorySize, [System.IO.MemoryMappedFiles.MemoryMappedFileAccess]::Read)
    }
}
try {
    $bytes = New-Object byte[] $headerSize
    [void]$view.ReadArray(0, $bytes, 0, $bytes.Length)
    $magic = [BitConverter]::ToUInt32($bytes, 0)
    $version = [BitConverter]::ToUInt32($bytes, 4)
    $state = [BitConverter]::ToInt32($bytes, 8)
    $eventWriteIndex = [BitConverter]::ToUInt32($bytes, 12)
    $dropped = [BitConverter]::ToUInt32($bytes, 16)
    $eventCount = [BitConverter]::ToUInt32($bytes, 20)
    $droppedNotifies = if ($bytes.Length -ge 28) { [BitConverter]::ToUInt32($bytes, 24) } else { 0 }
    $publishedNotifies = if ($bytes.Length -ge 32) { [BitConverter]::ToUInt32($bytes, 28) } else { 0 }
    $writeSequence = if ($bytes.Length -ge 36) { [BitConverter]::ToUInt32($bytes, 32) } else { 0 }
    "magic=0x{0:X8} version={1} state={2} writeIndex={3} summaryDropped={4} notifyDropped={5} notifyPublished={6} writeSeq={7} events={8}" -f $magic, $version, $state, $eventWriteIndex, $dropped, $droppedNotifies, $publishedNotifies, $writeSequence, $eventCount

    $startSlot = if ($eventCount -ge $maxEventCount) { $eventWriteIndex % $maxEventCount } else { 0 }
    for ($index = 0; $index -lt $eventCount -and $index -lt $maxEventCount; $index++) {
        $slot = ($startSlot + $index) % $maxEventCount
        $offset = $headerSize + ($slot * $eventRecordSize)
        $eventType = $view.ReadInt32($offset)
        $levelTime = $view.ReadInt32($offset + 4)
        $ownerId = $view.ReadUInt32($offset + 8)
        $stringValue = $view.ReadUInt32($offset + 12)
        $tick = $view.ReadUInt32($offset + 16)
        $nameBytes = New-Object byte[] 64
        [void]$view.ReadArray($offset + 20, $nameBytes, 0, $nameBytes.Length)
        $zero = [Array]::IndexOf($nameBytes, [byte]0)
        if ($zero -lt 0) {
            $zero = $nameBytes.Length
        }

        $name = [Text.Encoding]::UTF8.GetString($nameBytes, 0, $zero)
        "event[{0}] type={1} value={2} owner={3} id={4} tick={5} name={6}" -f $index, $eventType, $levelTime, $ownerId, $stringValue, $tick, $name
    }
}
finally {
    if ($null -ne $view) {
        $view.Dispose()
    }
    $mmf.Dispose()
}
