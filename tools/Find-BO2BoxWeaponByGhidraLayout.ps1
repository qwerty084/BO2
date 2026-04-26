param(
    [int]$ProcessId = 0,
    [int[]]$OwnerIds = @(),
    [int]$MaxParentId = 16383,
    [int]$MaxStringId = 8192,
    [int]$MaxHits = 80,
    [int[]]$Instances = @(0, 1),
    [int]$MaxChildId = 131071,
    [switch]$DumpOwnerFields,
    [switch]$ScanWeaponStringValues
)

$ErrorActionPreference = 'Stop'

Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

public static class Bo2GhidraLayoutReader
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

$scriptStringDataPointer = [uint32]0x02BF83A4
$scriptStringStride = 0x18
$scriptStringTextOffset = 4

# Ghidra 12.0.4 static pass on Steam t6zm.exe:
#   FindVariable(inst, parent, name) reads these per-instance pointer slots.
#   child = childVariablesBase + (childId * 0x1c)
#   child + 0x00 = value dword
#   child + 0x08 = next hash child id
#   child + 0x10 = (parentId << 16) | (name >> 8)
$childBucketsPointerSlotBase = [uint32]0x02DEFB00
$childVariablesPointerSlotBase = [uint32]0x02DEFB80
$scriptInstancePointerStride = 0x200
$scriptChildVariableStride = 0x1c
$scriptChildVariableKeyOffset = 0x10
$scriptChildVariableNextOffset = 0x08
$scriptChildHashMask = 0x1ffff

if ($ProcessId -le 0) {
    $process = Get-Process -Name t6zm -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($null -eq $process) {
        throw 'Pass -ProcessId or start t6zm.exe first.'
    }

    $ProcessId = $process.Id
}

$script:handle = [Bo2GhidraLayoutReader]::OpenProcess(0x0010 -bor 0x0400, $false, $ProcessId)
if ($script:handle -eq [IntPtr]::Zero) {
    $errorCode = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
    throw "OpenProcess failed for PID $ProcessId. Win32 error: $errorCode"
}

function Format-Hex32 {
    param([uint64]$Value)
    '0x{0:X8}' -f ($Value -band 0xffffffffL)
}

function Read-ProcessBytes {
    param(
        [uint32]$Address,
        [int]$Count,
        [switch]$Quiet
    )

    $buffer = New-Object byte[] $Count
    $bytesRead = [IntPtr]::Zero
    $ok = [Bo2GhidraLayoutReader]::ReadProcessMemory(
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

    throw "Could not resolve script string '$Name'. Retry with -MaxStringId 65536."
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

function Get-InstanceLayout {
    param([int]$Inst)

    $slotOffset = [uint32]($Inst * $scriptInstancePointerStride)
    $bucketSlot = [uint32]($childBucketsPointerSlotBase + $slotOffset)
    $childSlot = [uint32]($childVariablesPointerSlotBase + $slotOffset)
    $bucketBase = Read-UInt32 -Address $bucketSlot -Quiet
    $childBase = Read-UInt32 -Address $childSlot -Quiet

    if ($null -eq $bucketBase -or $null -eq $childBase -or $bucketBase -lt 0x00100000 -or $childBase -lt 0x00100000) {
        return $null
    }

    [pscustomobject]@{
        Inst = $Inst
        BucketSlot = $bucketSlot
        ChildSlot = $childSlot
        BucketBase = [uint32]$bucketBase
        ChildBase = [uint32]$childBase
    }
}

function Find-ScriptVariable {
    param(
        [pscustomobject]$Layout,
        [uint32]$ParentId,
        [uint32]$NameId
    )

    $bucketIndex = (($ParentId * 0x65) + $NameId) -band $scriptChildHashMask
    $bucketAddress = [uint32]($Layout.BucketBase + ($bucketIndex * 4))
    $childId = Read-UInt32 -Address $bucketAddress -Quiet
    if ($null -eq $childId -or $childId -eq 0) {
        return $null
    }

    $expectedKey = (($ParentId -band 0xffff) -shl 16) -bor (($NameId -shr 8) -band 0xffff)
    $visited = 0
    while ($childId -ne 0 -and $visited -lt 256) {
        $childAddress = [uint32]($Layout.ChildBase + ($childId * $scriptChildVariableStride))
        $raw = Read-ProcessBytes -Address $childAddress -Count $scriptChildVariableStride -Quiet
        if ($null -eq $raw) {
            return $null
        }

        $key = [BitConverter]::ToUInt32($raw, $scriptChildVariableKeyOffset)
        $next = [BitConverter]::ToUInt32($raw, $scriptChildVariableNextOffset)
        if ($key -eq $expectedKey) {
            return [pscustomobject]@{
                Inst = $Layout.Inst
                ParentId = $ParentId
                NameId = $NameId
                ChildId = [uint32]$childId
                Address = $childAddress
                Value = [BitConverter]::ToUInt32($raw, 0)
                Next = [uint32]$next
                Key = [uint32]$key
                Raw = (($raw | ForEach-Object { $_.ToString('X2') }) -join ' ')
            }
        }

        $childId = $next
        $visited++
    }

    return $null
}

function Resolve-FieldNameFromChild {
    param(
        [pscustomobject]$Layout,
        [uint32]$ParentId,
        [uint32]$ChildId,
        [uint32]$NameHi,
        [byte[]]$StringTable
    )

    for ($nameLo = 0; $nameLo -le 255; $nameLo++) {
        $nameId = [uint32](($NameHi -shl 8) -bor $nameLo)
        $hit = Find-ScriptVariable -Layout $Layout -ParentId $ParentId -NameId $nameId
        if ($null -eq $hit -or $hit.ChildId -ne $ChildId) {
            continue
        }

        if ($nameId -ge 0x10000) {
            $scriptStringId = [uint32]($nameId - 0x10000)
            $scriptString = Read-ScriptString -StringTable $StringTable -StringId $scriptStringId
            if ($scriptString.Length -gt 0) {
                return 'field:{0}({1})' -f $scriptString, $nameId
            }
        }

        $plainString = Read-ScriptString -StringTable $StringTable -StringId $nameId
        if ($plainString.Length -gt 0) {
            return 'name:{0}({1})' -f $plainString, $nameId
        }

        return 'nameId:{0}' -f $nameId
    }

    return 'nameHi:{0}' -f $NameHi
}

function Read-ChildTable {
    param([pscustomobject]$Layout)

    $count = $MaxChildId + 1
    $byteCount = $count * $scriptChildVariableStride
    Read-ProcessBytes -Address $Layout.ChildBase -Count $byteCount
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

    [pscustomobject]@{
        ChildId = $ChildId
        Value = [BitConverter]::ToUInt32($ChildTable, $offset)
        Next = [BitConverter]::ToUInt32($ChildTable, $offset + $scriptChildVariableNextOffset)
        Key = [BitConverter]::ToUInt32($ChildTable, $offset + $scriptChildVariableKeyOffset)
        Raw = (($ChildTable[$offset..($offset + $scriptChildVariableStride - 1)] | ForEach-Object { $_.ToString('X2') }) -join ' ')
    }
}

function Write-FieldHit {
    param(
        [string]$Label,
        [pscustomobject]$Hit,
        [byte[]]$StringTable
    )

    if ($null -eq $Hit) {
        return
    }

    $text = Read-ScriptString -StringTable $StringTable -StringId $Hit.Value
    'hit inst={0} parent={1} field={2} child={3} addr={4} value={5} string="{6}" key={7} raw={8}' -f `
        $Hit.Inst,
        $Hit.ParentId,
        $Label,
        $Hit.ChildId,
        (Format-Hex32 $Hit.Address),
        $Hit.Value,
        $text,
        (Format-Hex32 $Hit.Key),
        $Hit.Raw
}

function Write-ChildEntry {
    param(
        [pscustomobject]$Layout,
        [pscustomobject]$Entry,
        [byte[]]$StringTable,
        [string]$Reason
    )

    $parentId = [uint32](($Entry.Key -shr 16) -band 0xffff)
    $nameHi = [uint32]($Entry.Key -band 0xffff)
    $fieldName = Resolve-FieldNameFromChild -Layout $Layout -ParentId $parentId -ChildId $Entry.ChildId -NameHi $nameHi -StringTable $StringTable
    $valueText = Read-ScriptString -StringTable $StringTable -StringId $Entry.Value
    'child {0} inst={1} parent={2} child={3} field={4} value={5} string="{6}" next={7} key={8} raw={9}' -f `
        $Reason,
        $Layout.Inst,
        $parentId,
        $Entry.ChildId,
        $fieldName,
        $Entry.Value,
        $valueText,
        $Entry.Next,
        (Format-Hex32 $Entry.Key),
        $Entry.Raw
}

try {
    $stringDataBase = Read-UInt32 -Address $scriptStringDataPointer
    'pid={0} stringDataBase={1}' -f $ProcessId, (Format-Hex32 $stringDataBase)
    $stringTable = Read-ProcessBytes -Address $stringDataBase -Count ($MaxStringId * $scriptStringStride)

    $weaponStringId = Resolve-ScriptStringId -StringTable $stringTable -Name 'weapon_string'
    $grabWeaponNameId = Resolve-ScriptStringId -StringTable $stringTable -Name 'grab_weapon_name'
    $zbarrierId = Resolve-ScriptStringId -StringTable $stringTable -Name 'zbarrier'
    $weaponStringFieldName = [uint32]($weaponStringId + 0x10000)
    $grabWeaponNameFieldName = [uint32]($grabWeaponNameId + 0x10000)
    $zbarrierFieldName = [uint32]($zbarrierId + 0x10000)

    'fieldIds weapon_string={0} grab_weapon_name={1} zbarrier={2}' -f $weaponStringId, $grabWeaponNameId, $zbarrierId
    'encodedFieldNames weapon_string={0} grab_weapon_name={1} zbarrier={2}' -f $weaponStringFieldName, $grabWeaponNameFieldName, $zbarrierFieldName

    $layouts = foreach ($inst in $Instances) {
        Get-InstanceLayout -Inst $inst
    }
    $layouts = @($layouts | Where-Object { $null -ne $_ })

    if ($layouts.Count -eq 0) {
        throw 'No readable script instance child table pointers were found.'
    }

    foreach ($layout in $layouts) {
        'layout inst={0} bucketSlot={1} bucketBase={2} childSlot={3} childBase={4} stride=0x1C' -f `
            $layout.Inst,
            (Format-Hex32 $layout.BucketSlot),
            (Format-Hex32 $layout.BucketBase),
            (Format-Hex32 $layout.ChildSlot),
            (Format-Hex32 $layout.ChildBase)
    }

    if ($OwnerIds.Count -gt 0) {
        'owner check: {0}' -f ($OwnerIds -join ',')
        foreach ($layout in $layouts) {
            foreach ($ownerId in $OwnerIds) {
                Write-FieldHit -Label 'weapon_string' -Hit (Find-ScriptVariable -Layout $layout -ParentId $ownerId -NameId $weaponStringId) -StringTable $stringTable
                Write-FieldHit -Label 'weapon_string.field' -Hit (Find-ScriptVariable -Layout $layout -ParentId $ownerId -NameId $weaponStringFieldName) -StringTable $stringTable
                Write-FieldHit -Label 'grab_weapon_name.field' -Hit (Find-ScriptVariable -Layout $layout -ParentId $ownerId -NameId $grabWeaponNameFieldName) -StringTable $stringTable
                $zbarrier = Find-ScriptVariable -Layout $layout -ParentId $ownerId -NameId $zbarrierFieldName
                Write-FieldHit -Label 'zbarrier' -Hit $zbarrier -StringTable $stringTable
                if ($null -ne $zbarrier -and $zbarrier.Value -gt 0) {
                    Write-FieldHit -Label 'zbarrier.weapon_string.field' -Hit (Find-ScriptVariable -Layout $layout -ParentId $zbarrier.Value -NameId $weaponStringFieldName) -StringTable $stringTable
                }
            }
        }
    }

    if ($DumpOwnerFields -and $OwnerIds.Count -gt 0) {
        'owner field dump maxChildId={0}' -f $MaxChildId
        foreach ($layout in $layouts) {
            $childTable = Read-ChildTable -Layout $layout
            $dumped = 0
            for ($childId = 1; $childId -le $MaxChildId; $childId++) {
                $entry = Read-ChildEntryFromTable -ChildTable $childTable -ChildId $childId
                if ($null -eq $entry -or $entry.Key -eq 0) {
                    continue
                }

                $parentId = [uint32](($entry.Key -shr 16) -band 0xffff)
                if ($OwnerIds -notcontains [int]$parentId) {
                    continue
                }

                Write-ChildEntry -Layout $layout -Entry $entry -StringTable $stringTable -Reason 'owner-field'
                $dumped++
                if ($dumped -ge $MaxHits) {
                    break
                }
            }

            if ($dumped -eq 0) {
                'owner field dump inst={0}: no fields found for owners {1}' -f $layout.Inst, ($OwnerIds -join ',')
            }
        }
    }

    if ($ScanWeaponStringValues) {
        'weapon-like string value scan maxChildId={0}' -f $MaxChildId
        $weaponLikeIds = @{}
        for ($id = 1; $id -lt $MaxStringId; $id++) {
            $text = Read-ScriptString -StringTable $stringTable -StringId ([uint32]$id)
            if ($text -match '(^|_)(ray|gun|galil|hamr|rpd|dsr|barrett|python|fiveseven|ak74u|mp5|pdw|type95|m27|mtar|fal|m8a1|s12|ksg|executioner|ballista|war_machine|rpg|smaw|crossbow|knife|teddy|bear|m1911|m14|olympia|m16|fnfal|spectre|cz75|commando|hk21|china_lake|dragunov|aug|famas|spas|hs10|stakeout|l96a1|m72|law|thundergun|winter|waffe)|_zm($|_)') {
                $weaponLikeIds[[uint32]$id] = $text
            }
        }

        'weapon-like script strings={0}' -f $weaponLikeIds.Count
        $hits = 0
        foreach ($layout in $layouts) {
            $childTable = Read-ChildTable -Layout $layout
            for ($childId = 1; $childId -le $MaxChildId; $childId++) {
                $entry = Read-ChildEntryFromTable -ChildTable $childTable -ChildId $childId
                if ($null -eq $entry -or $entry.Key -eq 0 -or -not $weaponLikeIds.ContainsKey([uint32]$entry.Value)) {
                    continue
                }

                Write-ChildEntry -Layout $layout -Entry $entry -StringTable $stringTable -Reason 'weapon-value'
                $hits++
                if ($hits -ge $MaxHits) {
                    'max weapon-value hits reached'
                    break
                }
            }

            if ($hits -ge $MaxHits) {
                break
            }
        }

        if ($hits -eq 0) {
            'No weapon-like script string values found in child variables.'
        }
    }

    'global scan: parents 1..{0}' -f $MaxParentId
    $hits = 0
    foreach ($layout in $layouts) {
        for ($parentId = 1; $parentId -le $MaxParentId; $parentId++) {
            foreach ($field in @(
                @{ Label = 'weapon_string.field'; Id = $weaponStringFieldName },
                @{ Label = 'grab_weapon_name.field'; Id = $grabWeaponNameFieldName }
            )) {
                $hit = Find-ScriptVariable -Layout $layout -ParentId $parentId -NameId $field.Id
                if ($null -eq $hit) {
                    continue
                }

                Write-FieldHit -Label $field.Label -Hit $hit -StringTable $stringTable
                $hits++
                if ($hits -ge $MaxHits) {
                    'max hits reached'
                    return
                }
            }

            $zbarrier = Find-ScriptVariable -Layout $layout -ParentId $parentId -NameId $zbarrierFieldName
            if ($null -ne $zbarrier -and $zbarrier.Value -gt 0) {
                Write-FieldHit -Label 'zbarrier' -Hit $zbarrier -StringTable $stringTable
                $hits++
                $weapon = Find-ScriptVariable -Layout $layout -ParentId $zbarrier.Value -NameId $weaponStringFieldName
                if ($null -ne $weapon) {
                    Write-FieldHit -Label 'zbarrier.weapon_string.field' -Hit $weapon -StringTable $stringTable
                    $hits++
                }

                if ($hits -ge $MaxHits) {
                    'max hits reached'
                    return
                }
            }
        }
    }

    if ($hits -eq 0) {
        'No target fields found with the Ghidra-proven child table layout.'
    }
}
finally {
    if ($script:handle -ne [IntPtr]::Zero) {
        [void][Bo2GhidraLayoutReader]::CloseHandle($script:handle)
    }
}
