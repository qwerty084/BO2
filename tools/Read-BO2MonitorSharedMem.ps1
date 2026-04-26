$ErrorActionPreference = 'Stop'

$maxEventCount = 128
$headerSize = 24
$eventRecordSize = 72
$sharedMemorySize = $headerSize + ($maxEventCount * $eventRecordSize)

$mmf = [System.IO.MemoryMappedFiles.MemoryMappedFile]::OpenExisting('BO2MonitorSharedMem')
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
    $bytes = New-Object byte[] 24
    [void]$view.ReadArray(0, $bytes, 0, $bytes.Length)
    $magic = [BitConverter]::ToUInt32($bytes, 0)
    $version = [BitConverter]::ToUInt32($bytes, 4)
    $state = [BitConverter]::ToInt32($bytes, 8)
    $dropped = [BitConverter]::ToUInt32($bytes, 16)
    $eventCount = [BitConverter]::ToUInt32($bytes, 20)
    "magic=0x{0:X8} version={1} state={2} dropped={3} events={4}" -f $magic, $version, $state, $dropped, $eventCount

    for ($index = 0; $index -lt $eventCount -and $index -lt $maxEventCount; $index++) {
        $offset = $headerSize + ($index * $eventRecordSize)
        $eventType = $view.ReadInt32($offset)
        $levelTime = $view.ReadInt32($offset + 4)
        $nameBytes = New-Object byte[] 64
        [void]$view.ReadArray($offset + 8, $nameBytes, 0, $nameBytes.Length)
        $zero = [Array]::IndexOf($nameBytes, [byte]0)
        if ($zero -lt 0) {
            $zero = $nameBytes.Length
        }

        $name = [Text.Encoding]::UTF8.GetString($nameBytes, 0, $zero)
        "event[{0}] type={1} value={2} name={3}" -f $index, $eventType, $levelTime, $name
    }
}
finally {
    if ($null -ne $view) {
        $view.Dispose()
    }
    $mmf.Dispose()
}
