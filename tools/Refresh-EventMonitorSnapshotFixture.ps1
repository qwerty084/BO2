[CmdletBinding()]
param(
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$fixtureProject = Join-Path $repoRoot 'tools\EventMonitorSnapshotFixture\EventMonitorSnapshotFixture.vcxproj'
$fixtureExe = Join-Path $repoRoot 'tools\EventMonitorSnapshotFixture\bin\Win32\Release\EventMonitorSnapshotFixture.exe'
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = Join-Path $repoRoot 'BO2.Tests\Fixtures\EventMonitorSnapshot.v6.wrapped.bin'
}

function Find-FirstExistingFile {
    param([string[]]$Candidates)

    foreach ($candidate in $Candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    return $null
}

function Invoke-VSWhere {
    param(
        [string[]]$Requires,
        [string]$Find
    )

    $vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (-not (Test-Path -LiteralPath $vswhere -PathType Leaf)) {
        return @()
    }

    $arguments = @('-latest', '-products', '*')
    foreach ($requirement in $Requires) {
        $arguments += @('-requires', $requirement)
    }

    $arguments += @('-find', $Find)
    return @(& $vswhere @arguments) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
}

function Find-MSBuild {
    $candidates = @()
    $candidates += $env:NativeMSBuildExe
    $candidates += Invoke-VSWhere -Requires @('Microsoft.Component.MSBuild') -Find 'MSBuild\Current\Bin\MSBuild.exe'
    $candidates += @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe"
    )

    $msbuild = Find-FirstExistingFile -Candidates $candidates
    if ($null -eq $msbuild) {
        throw 'Could not find native MSBuild.exe. Install Visual Studio with Desktop development with C++, or set NativeMSBuildExe.'
    }

    return $msbuild
}

$msbuild = Find-MSBuild

Write-Host "Building Event Monitor snapshot fixture generator with $msbuild"
& $msbuild $fixtureProject /t:Build /p:Configuration=Release /p:Platform=Win32 /m /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Fixture generator build failed with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $fixtureExe -PathType Leaf)) {
    throw "Fixture generator executable was not produced: $fixtureExe"
}

Write-Host "Writing Event Monitor snapshot fixture to $OutputPath"
& $fixtureExe $OutputPath
if ($LASTEXITCODE -ne 0) {
    throw "Fixture generator failed with exit code $LASTEXITCODE."
}
