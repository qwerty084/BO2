[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release', 'ReleaseWithVmNotifyHook')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
$testProject = Join-Path $repoRoot 'BO2.NativeTests\BO2.NativeTests.vcxproj'
$testAssembly = Join-Path $repoRoot "BO2.NativeTests\bin\Win32\$Configuration\BO2.NativeTests.dll"
$runSettings = Join-Path $repoRoot 'tools\NativeTests.runsettings'
$resultsDirectory = Join-Path $repoRoot 'TestResults\Native'
$buildLabel = "$Configuration|Win32"
$trxLogFileName = "BO2.NativeTests.$Configuration.trx"
$trxLogger = "trx;LogFileName=$trxLogFileName"

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

function Find-VSTestConsole {
    $candidates = @()
    $candidates += $env:VSTestConsoleExe
    $candidates += Invoke-VSWhere -Requires @('Microsoft.VisualStudio.PackageGroup.TestTools.Core') -Find 'Common7\IDE\Extensions\TestPlatform\vstest.console.exe'
    $candidates += @(
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\Common7\IDE\Extensions\TestPlatform\vstest.console.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\Common7\IDE\Extensions\TestPlatform\vstest.console.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Enterprise\Common7\IDE\Extensions\TestPlatform\vstest.console.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Professional\Common7\IDE\Extensions\TestPlatform\vstest.console.exe",
        "${env:ProgramFiles}\Microsoft Visual Studio\18\Community\Common7\IDE\Extensions\TestPlatform\vstest.console.exe"
    )

    $vstest = Find-FirstExistingFile -Candidates $candidates
    if ($null -eq $vstest) {
        throw 'Could not find vstest.console.exe. Install Visual Studio test tools, or set VSTestConsoleExe.'
    }

    return $vstest
}

$msbuild = Find-MSBuild
$vstest = Find-VSTestConsole

Write-Host "Native test configuration: $buildLabel"
Write-Host "Native test project: $testProject"
Write-Host "Native test assembly: $testAssembly"
Write-Host "Native test results: $(Join-Path $resultsDirectory $trxLogFileName)"
Write-Host "Building native tests ($buildLabel) with $msbuild"
& $msbuild $testProject /t:Build /p:Configuration=$Configuration /p:Platform=Win32 /m /v:minimal
if ($LASTEXITCODE -ne 0) {
    throw "Native test build failed for $buildLabel with exit code $LASTEXITCODE."
}

if (-not (Test-Path -LiteralPath $testAssembly -PathType Leaf)) {
    throw "Native test assembly was not produced for $($buildLabel): $testAssembly. Ensure Visual C++ MSBuild targets and Microsoft Native Unit Test tools are installed."
}

New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null

Write-Host "Running native tests ($buildLabel) with $vstest"
& $vstest `
    $testAssembly `
    /Platform:x86 `
    /InIsolation `
    /Settings:$runSettings `
    "/Logger:$trxLogger" `
    /ResultsDirectory:$resultsDirectory
if ($LASTEXITCODE -ne 0) {
    throw "Native tests failed for $buildLabel with exit code $LASTEXITCODE."
}
