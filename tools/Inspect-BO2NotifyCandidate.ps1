param(
    [int]$ProcessId,
    [string]$CdbPath = "${env:ProgramFiles(x86)}\Windows Kits\10\Debuggers\x86\cdb.exe"
)

$ErrorActionPreference = 'Stop'

if (-not (Test-Path -LiteralPath $CdbPath)) {
    throw "cdb.exe not found: $CdbPath"
}

$commands = @(
    'lm m t6zm',
    '!address 008f3620',
    'u 008f35d0 008f3660',
    'db 008f35d0 L90',
    '!address 008f31d0',
    'u 008f31d0 L50',
    'db 008f31d0 L20',
    '!address 00532230',
    'u 00532230 L50',
    'db 00532230 L30',
    '!address 00422d10',
    'u 00422d10 L50',
    'db 00422d10 L30',
    'q'
) -join '; '

& $CdbPath -pv -p $ProcessId -c $commands
