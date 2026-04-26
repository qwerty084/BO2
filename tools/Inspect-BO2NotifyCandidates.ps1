param(
    [int]$ProcessId
)

$ErrorActionPreference = 'Stop'

if (-not $ProcessId) {
    $ProcessId = (Get-Process -Name t6zm -ErrorAction Stop | Select-Object -First 1).Id
}

$cdb = "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x86\cdb.exe"
if (-not (Test-Path -LiteralPath $cdb)) {
    throw "cdb.exe not found: $cdb"
}

$commands = @(
    'lm m t6zm',
    '!address 008f3620',
    'db 008f3620 L30',
    'u 008f3600 008f3660',
    '!address 008f31d0',
    'db 008f31d0 L30',
    'u 008f31d0 008f3260',
    '!address 00532230',
    'db 00532230 L30',
    'u 00532230 00532290',
    'q'
) -join '; '

& $cdb -pv -p $ProcessId -c $commands
