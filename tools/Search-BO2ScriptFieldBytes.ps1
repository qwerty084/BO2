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
    public struct MEMORY_BASIC_INFORMATION32
    {
        public uint BaseAddress;
        public uint AllocationBase;
        public uint AllocationProtect;
        public uint RegionSize;
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
        out MEMORY_BASIC_INFORMATION32 memoryInformation,
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

$handle = [Bo2MemorySearchNative]::OpenProcess($processAccess, $false, $ProcessId)
if ($handle -eq [IntPtr]::Zero) {
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
        $text = Get-NullTerminatedAscii -Buffer $StringTable -Offset $offset -MaxLength 20
        if ($text -eq $Name) {
            return [uint32]$id
        }
    }

    throw "Could not resolve script string '$Name'. Retry with -MaxStringId 65536."
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

    $weaponStringId = Resolve-ScriptStringId -StringTable $stringTable -Name 'weapon_string'
    $grabWeaponNameId = Resolve-ScriptStringId -StringTable $stringTable -Name 'grab_weapon_name'
    $zbarrierId = Resolve-ScriptStringId -StringTable $stringTable -Name 'zbarrier'

    $patterns = @()
    $patterns += New-PatternsForField -Name 'weapon_string' -Id $weaponStringId
    $patterns += New-PatternsForField -Name 'grab_weapon_name' -Id $grabWeaponNameId
    $patterns += New-PatternsForField -Name 'zbarrier' -Id $zbarrierId

    'pid={0} scan={1}-{2} stringDataBase={3}' -f `
        $ProcessId,
        (Format-Hex32 $StartAddress),
        (Format-Hex32 $EndAddress),
        (Format-Hex32 $stringDataBase)
    'fieldIds weapon_string={0} grab_weapon_name={1} zbarrier={2}' -f `
        $weaponStringId,
        $grabWeaponNameId,
        $zbarrierId

    $hitCount = 0
    $address = $StartAddress
    while ($address -lt $EndAddress -and $hitCount -lt $MaxHits) {
        $memoryInformation = New-Object Bo2MemorySearchNative+MEMORY_BASIC_INFORMATION32
        $querySize = [Runtime.InteropServices.Marshal]::SizeOf([type]'Bo2MemorySearchNative+MEMORY_BASIC_INFORMATION32')
        $queryResult = [Bo2MemorySearchNative]::VirtualQueryEx(
            $handle,
            [IntPtr]([int64]$address),
            [ref]$memoryInformation,
            $querySize)

        if ($queryResult -eq 0 -or $memoryInformation.RegionSize -eq 0) {
            break
        }

        $base = [uint32]$memoryInformation.BaseAddress
        $regionEnd64 = [uint64]$base + [uint64]$memoryInformation.RegionSize
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
    [void][Bo2MemorySearchNative]::CloseHandle($handle)
}
