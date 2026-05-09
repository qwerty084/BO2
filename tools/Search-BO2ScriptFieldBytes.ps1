<#
.SYNOPSIS
Searches the live t6zm.exe address space for byte patterns derived from script field string IDs.

.DESCRIPTION
This is a read-only runtime research helper. It opens the target with
PROCESS_VM_READ | PROCESS_QUERY_INFORMATION, resolves known script strings from
the live string table, and scans committed readable regions for little-endian
forms of those IDs.

The script is useful only after BO2 Zombies has loaded far enough to initialize
the script string table at 0x02BF83A4. Field names can be map/state dependent:
missing fields are reported and skipped, not treated as scanner failure.

.EXAMPLE
.\tools\Search-BO2ScriptFieldBytes.ps1 -ProcessId 1234 -MaxStringId 65536

Scans the default 0x02000000..0x04000000 range for available known field IDs.

.EXAMPLE
.\tools\Search-BO2ScriptFieldBytes.ps1 -IncludeU16 -MaxHits 50

Uses the first running t6zm.exe process and also searches short two-byte forms.

.NOTES
Expected output starts with pid, scan range, string table base, and one fieldIds
line per resolved or missing field. No hits can mean the process is in the wrong
state, the field name is not loaded, the scan range is too narrow, or the field
is not represented by a searched byte encoding.
#>
param(
    [int]$ProcessId = 0,
    [uint32]$StartAddress = 0x02000000,
    [uint32]$EndAddress = 0x04000000,
    [int]$MaxStringId = 8192,
    [int]$MaxHits = 200,
    [switch]$IncludeU16
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Bo2MemorySearchNative
{
    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORY_BASIC_INFORMATION
    {
        public IntPtr BaseAddress;
        public IntPtr AllocationBase;
        public uint AllocationProtect;
        public UIntPtr RegionSize;
        public uint State;
        public uint Protect;
        public uint Type;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(int access, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern int VirtualQueryEx(
        IntPtr process,
        IntPtr address,
        out MEMORY_BASIC_INFORMATION memoryInformation,
        int length);

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
$memCommit = 0x1000
$pageNoAccess = 0x01
$pageGuard = 0x100
$scriptStringDataPointer = [uint32]0x02BF83A4
$scriptStringStride = 0x18
$scriptStringTextOffset = 4

if ($ProcessId -le 0) {
    $process = Get-Process -Name t6zm -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $process) {
        throw 'Pass -ProcessId or start t6zm.exe first.'
    }

    $ProcessId = $process.Id
}

$script:handle = [Bo2MemorySearchNative]::OpenProcess($processAccess, $false, $ProcessId)
if ($script:handle -eq [IntPtr]::Zero) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "OpenProcess failed for PID $ProcessId. Win32 error: $errorCode"
}

function Format-Hex32 {
    param([uint32]$Value)

    '0x{0:X8}' -f $Value
}

function Read-ProcessBytes {
    param(
        [uint32]$Address,
        [int]$Count
    )

    $buffer = New-Object byte[] $Count
    $bytesRead = [IntPtr]::Zero
    $ok = [Bo2MemorySearchNative]::ReadProcessMemory(
        $script:handle,
        [IntPtr]([int64]$Address),
        $buffer,
        $Count,
        [ref]$bytesRead)

    if (-not $ok -or $bytesRead.ToInt64() -le 0) {
        return $null
    }

    if ($bytesRead.ToInt64() -ne $Count) {
        $shortBuffer = New-Object byte[] $bytesRead.ToInt32()
        [Array]::Copy($buffer, $shortBuffer, $shortBuffer.Length)
        return ,$shortBuffer
    }

    return ,$buffer
}

function Read-UInt32 {
    param([uint32]$Address)

    $bytes = Read-ProcessBytes -Address $Address -Count 4
    if ($null -eq $bytes -or $bytes.Length -lt 4) {
        throw "Could not read uint32 at $(Format-Hex32 $Address)"
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

function Resolve-ScriptStringId {
    param(
        [byte[]]$StringTable,
        [string]$Name
    )

    for ($id = 1; $id -lt $MaxStringId; $id++) {
        $offset = ($id * $scriptStringStride) + $scriptStringTextOffset
        $text = Get-NullTerminatedAscii -Buffer $StringTable -Offset $offset -MaxLength 96
        if ($text -eq $Name) {
            return [uint32]$id
        }
    }

    return $null
}

function Convert-ToHexBytes {
    param(
        [byte[]]$Buffer,
        [int]$Offset,
        [int]$Count
    )

    $start = [Math]::Max(0, $Offset)
    $end = [Math]::Min($Buffer.Length, $Offset + $Count)
    if ($end -le $start) {
        return ''
    }

    ($Buffer[$start..($end - 1)] | ForEach-Object { '{0:X2}' -f $_ }) -join ' '
}

function Find-PatternHits {
    param(
        [byte[]]$Buffer,
        [byte[]]$Pattern
    )

    if ($Pattern.Length -eq 0 -or $Buffer.Length -lt $Pattern.Length) {
        return @()
    }

    $hits = New-Object System.Collections.Generic.List[int]
    $lastStart = $Buffer.Length - $Pattern.Length
    for ($index = 0; $index -le $lastStart; $index++) {
        $matched = $true
        for ($patternIndex = 0; $patternIndex -lt $Pattern.Length; $patternIndex++) {
            if ($Buffer[$index + $patternIndex] -ne $Pattern[$patternIndex]) {
                $matched = $false
                break
            }
        }

        if ($matched) {
            $hits.Add($index)
        }
    }

    return $hits
}

function New-PatternsForField {
    param(
        [string]$Name,
        [uint32]$Id
    )

    function Convert-UInt32Pattern {
        param([uint32]$Value)

        [byte[]]@(
            [byte]($Value -band 0xFF),
            [byte](($Value -shr 8) -band 0xFF),
            [byte](($Value -shr 16) -band 0xFF),
            [byte](($Value -shr 24) -band 0xFF))
    }

    $raw = Convert-UInt32Pattern -Value $Id
    $encodedShift8 = Convert-UInt32Pattern -Value ([uint32]($Id -shl 8))
    $encodedShift8Flag1 = Convert-UInt32Pattern -Value ([uint32](($Id -shl 8) -bor 1))

    $patterns = @()
    if ($IncludeU16) {
        $patterns += @{
            Name = "$Name-u16"
            Bytes = [byte[]]@($raw[0], $raw[1])
        }
    }

    $patterns += @(
        @{
            Name = "$Name-u24-name"
            Bytes = [byte[]]@($raw[0], $raw[1], $raw[2])
        },
        @{
            Name = "$Name-u32"
            Bytes = $raw
        },
        @{
            Name = "$Name-shift8-u32"
            Bytes = $encodedShift8
        },
        @{
            Name = "$Name-shift8-flag1-u32"
            Bytes = $encodedShift8Flag1
        }
    )

    return $patterns
}

try {
    $stringDataBase = Read-UInt32 -Address $scriptStringDataPointer
    $stringTable = Read-ProcessBytes -Address $stringDataBase -Count ($MaxStringId * $scriptStringStride)
    if ($null -eq $stringTable) {
        throw "Could not read script string table at $(Format-Hex32 $stringDataBase)"
    }

    $patterns = @()
    $fieldNames = @('weapon_string', 'grab_weapon_name', 'zbarrier')
    $fieldIds = [ordered]@{}
    foreach ($fieldName in $fieldNames) {
        $fieldId = Resolve-ScriptStringId -StringTable $stringTable -Name $fieldName
        $fieldIds[$fieldName] = $fieldId
        if ($null -ne $fieldId) {
            $patterns += New-PatternsForField -Name $fieldName -Id $fieldId
        }
    }

    if ($patterns.Count -eq 0) {
        throw "None of the target field names were resolved from the live string table. Retry in-game with -MaxStringId 65536."
    }

    'pid={0} scan={1}-{2} stringDataBase={3}' -f `
        $ProcessId,
        (Format-Hex32 $StartAddress),
        (Format-Hex32 $EndAddress),
        (Format-Hex32 $stringDataBase)
    foreach ($entry in $fieldIds.GetEnumerator()) {
        $value = if ($null -eq $entry.Value) { '<not found>' } else { $entry.Value }
        'fieldIds {0}={1}' -f $entry.Key, $value
    }

    $hitCount = 0
    $address = $StartAddress
    while ($address -lt $EndAddress -and $hitCount -lt $MaxHits) {
        $memoryInformation = New-Object Bo2MemorySearchNative+MEMORY_BASIC_INFORMATION
        $querySize = [Runtime.InteropServices.Marshal]::SizeOf([type]'Bo2MemorySearchNative+MEMORY_BASIC_INFORMATION')
        $queryResult = [Bo2MemorySearchNative]::VirtualQueryEx(
            $script:handle,
            [IntPtr]([int64]$address),
            [ref]$memoryInformation,
            $querySize)

        if ($queryResult -eq 0 -or $memoryInformation.RegionSize -eq 0) {
            break
        }

        $base = [uint32]$memoryInformation.BaseAddress.ToInt64()
        $regionEnd64 = [uint64]$base + $memoryInformation.RegionSize.ToUInt64()
        $regionEnd = if ($regionEnd64 -gt [uint64][uint32]::MaxValue) { [uint32]::MaxValue } else { [uint32]$regionEnd64 }
        $readStart = [uint32][Math]::Max([uint64]$base, [uint64]$StartAddress)
        $readEnd = [uint32][Math]::Min([uint64]$regionEnd, [uint64]$EndAddress)

        $readable = $memoryInformation.State -eq $memCommit `
            -and (($memoryInformation.Protect -band $pageNoAccess) -eq 0) `
            -and (($memoryInformation.Protect -band $pageGuard) -eq 0) `
            -and $readEnd -gt $readStart

        if ($readable) {
            $regionSize = [int]([uint64]$readEnd - [uint64]$readStart)
            $buffer = Read-ProcessBytes -Address $readStart -Count $regionSize
            if ($null -ne $buffer) {
                foreach ($pattern in $patterns) {
                    $hits = Find-PatternHits -Buffer $buffer -Pattern $pattern.Bytes
                    foreach ($hit in $hits) {
                        $absolute = [uint32]([uint64]$readStart + [uint64]$hit)
                        $context = Convert-ToHexBytes -Buffer $buffer -Offset ($hit - 12) -Count 32
                        '{0} {1} context={2}' -f (Format-Hex32 $absolute), $pattern.Name, $context
                        $hitCount++
                        if ($hitCount -ge $MaxHits) {
                            break
                        }
                    }

                    if ($hitCount -ge $MaxHits) {
                        break
                    }
                }
            }
        }

        if ($regionEnd -le $address) {
            break
        }

        $address = $regionEnd
    }

    if ($hitCount -eq 0) {
        'No field ID byte patterns found in the scanned range.'
    }
}
finally {
    if ($script:handle -ne [IntPtr]::Zero) {
        [void][Bo2MemorySearchNative]::CloseHandle($script:handle)
    }
}
