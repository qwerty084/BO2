[CmdletBinding()]
param(
    [string]$PackageDirectory = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'artifacts\msix'),

    [string]$DiagnosticsDirectory = (Join-Path (Resolve-Path (Join-Path $PSScriptRoot '..')).Path 'TestResults\PackageSmoke'),

    [int]$LaunchTimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$diagnosticsRoot = $null
$installedPackage = $null
$importedCertificates = @()
$launchedProcessId = 0
$smokeWindow = $null
$packageIdentityName = $null
$transcriptStarted = $false
$smokeStartedAt = Get-Date

function Resolve-RequiredDirectory {
    param([string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Container)) {
        throw "Directory '$Path' does not exist."
    }

    return (Resolve-Path -LiteralPath $Path).Path
}

function Add-RequiredUiAutomationAssemblies {
    try {
        Add-Type -AssemblyName UIAutomationTypes
        Add-Type -AssemblyName UIAutomationClient
        return
    } catch {
        $frameworkRoots = @(
            Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\WPF',
            Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\WPF'
        )

        foreach ($frameworkRoot in $frameworkRoots) {
            $typesPath = Join-Path $frameworkRoot 'UIAutomationTypes.dll'
            $clientPath = Join-Path $frameworkRoot 'UIAutomationClient.dll'

            if ((Test-Path -LiteralPath $typesPath -PathType Leaf) -and
                (Test-Path -LiteralPath $clientPath -PathType Leaf)) {
                Add-Type -Path $typesPath
                Add-Type -Path $clientPath
                return
            }
        }

        throw 'Could not load UI Automation assemblies required for packaged app smoke testing.'
    }
}

Add-RequiredUiAutomationAssemblies

function Import-WindowsPackageModules {
    if ($null -eq (Get-Command -Name Add-AppxPackage -ErrorAction SilentlyContinue)) {
        Import-Module -Name Appx -ErrorAction Stop
    }

    if ($null -eq (Get-Command -Name Add-AppxPackage -ErrorAction SilentlyContinue)) {
        throw 'Add-AppxPackage is required to install the MSIX package. Run this script on Windows with the Appx module available.'
    }
}

Import-WindowsPackageModules

function Add-ApplicationActivationManagerType {
    if ($null -ne ('BO2.PackageSmoke.ApplicationActivationManager' -as [type])) {
        return
    }

    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;

namespace BO2.PackageSmoke
{
    [Flags]
    public enum ActivateOptions
    {
        None = 0,
        DesignMode = 1,
        NoErrorUI = 2,
        NoSplashScreen = 4
    }

    [ComImport]
    [Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")]
    public class ApplicationActivationManager
    {
    }

    [ComImport]
    [Guid("2e941141-7f97-4756-ba1d-9decde894a3d")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IApplicationActivationManager
    {
        [PreserveSig]
        int ActivateApplication(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments,
            ActivateOptions options,
            out int processId);

        [PreserveSig]
        int ActivateForFile(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            [MarshalAs(UnmanagedType.LPWStr)] string verb,
            out int processId);

        [PreserveSig]
        int ActivateForProtocol(
            [MarshalAs(UnmanagedType.LPWStr)] string appUserModelId,
            IntPtr itemArray,
            out int processId);
    }
}
'@
}

function Find-BuildPackage {
    param([string]$PackageRoot)

    $packages = @(Get-ChildItem -Path $PackageRoot -Recurse -File -Filter 'BO2_*_x86.msix')
    if ($packages.Count -ne 1) {
        throw "Expected exactly one BO2 x86 MSIX package under '$PackageRoot', found $($packages.Count)."
    }

    return $packages[0].FullName
}

function Get-PackageDependencies {
    param(
        [string]$PackageRoot,
        [string]$PackagePath
    )

    $dependencyExtensions = @('.appx', '.msix', '.appxbundle', '.msixbundle')
    $excludedArchitectureSegments = @('arm', 'arm64', 'x64')
    return @(
        Get-ChildItem -Path $PackageRoot -Recurse -File |
            Where-Object {
                $_.FullName -ne $PackagePath -and
                $dependencyExtensions -contains $_.Extension.ToLowerInvariant() -and
                $null -eq (
                    $_.FullName.Split([System.IO.Path]::DirectorySeparatorChar) |
                        Where-Object { $excludedArchitectureSegments -contains $_.ToLowerInvariant() } |
                        Select-Object -First 1
                )
            } |
            ForEach-Object { $_.FullName }
    )
}

function Read-MsixManifest {
    param([string]$PackagePath)

    Add-Type -AssemblyName System.IO.Compression.FileSystem

    $archive = [System.IO.Compression.ZipFile]::OpenRead($PackagePath)
    try {
        $manifestEntry = $archive.GetEntry('AppxManifest.xml')
        if ($null -eq $manifestEntry) {
            throw "Package '$PackagePath' does not contain AppxManifest.xml."
        }

        $manifestStream = $manifestEntry.Open()
        try {
            $reader = [System.IO.StreamReader]::new($manifestStream)
            try {
                [xml]$manifest = $reader.ReadToEnd()
            } finally {
                $reader.Dispose()
            }
        } finally {
            $manifestStream.Dispose()
        }

        $namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
        $namespaceManager.AddNamespace('appx', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')

        $identityNode = $manifest.SelectSingleNode('/appx:Package/appx:Identity', $namespaceManager)
        $applicationNode = $manifest.SelectSingleNode('/appx:Package/appx:Applications/appx:Application', $namespaceManager)
        $displayNameNode = $manifest.SelectSingleNode('/appx:Package/appx:Properties/appx:DisplayName', $namespaceManager)

        if ($null -eq $identityNode -or [string]::IsNullOrWhiteSpace($identityNode.Name)) {
            throw "Package identity name was not found in '$PackagePath'."
        }

        if ($null -eq $applicationNode -or [string]::IsNullOrWhiteSpace($applicationNode.Id)) {
            throw "Package application id was not found in '$PackagePath'."
        }

        $displayName = 'BO2'
        if ($null -ne $displayNameNode) {
            $displayName = $displayNameNode.InnerText
        }

        return [pscustomobject]@{
            IdentityName = $identityNode.Name
            Publisher = $identityNode.Publisher
            Version = $identityNode.Version
            ApplicationId = $applicationNode.Id
            DisplayName = $displayName
        }
    } finally {
        $archive.Dispose()
    }
}

function Find-TestCertificate {
    param([string]$PackageRoot)

    $certificates = @(Get-ChildItem -Path $PackageRoot -Recurse -File -Filter '*.cer')
    if ($certificates.Count -eq 0) {
        return $null
    }

    $preferred = @($certificates | Where-Object { $_.Name -eq 'BO2-TestSigning.cer' })
    if ($preferred.Count -eq 1) {
        return $preferred[0].FullName
    }

    if ($certificates.Count -eq 1) {
        return $certificates[0].FullName
    }

    throw "Expected one test signing certificate under '$PackageRoot', found $($certificates.Count)."
}

function Test-CertificateInCurrentUserStore {
    param(
        [string]$StoreName,
        [string]$Thumbprint
    )

    $parsedStoreName = [System.Enum]::Parse(
        [System.Security.Cryptography.X509Certificates.StoreName],
        $StoreName)
    $store = [System.Security.Cryptography.X509Certificates.X509Store]::new(
        $parsedStoreName,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::CurrentUser)

    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try {
        $matchingCertificates = $store.Certificates.Find(
            [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
            $Thumbprint,
            $false)
        return $matchingCertificates.Count -gt 0
    } finally {
        $store.Close()
    }
}

function Invoke-CertUtil {
    param([string[]]$Arguments)

    $certUtilPath = Join-Path $env:WINDIR 'System32\certutil.exe'
    if (-not (Test-Path -LiteralPath $certUtilPath -PathType Leaf)) {
        throw "certutil.exe was not found at '$certUtilPath'."
    }

    $output = & $certUtilPath @Arguments 2>&1
    $exitCode = $LASTEXITCODE
    foreach ($line in $output) {
        Write-Host $line
    }

    if ($exitCode -ne 0) {
        throw "certutil.exe failed with exit code $exitCode for arguments: $($Arguments -join ' ')"
    }
}

function Import-TestCertificate {
    param([string]$CertificatePath)

    if ([string]::IsNullOrWhiteSpace($CertificatePath)) {
        return @()
    }

    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($CertificatePath)
    $imported = @()
    # MSIX sideloading trusts package signing certificates from TrustedPeople.
    # Importing CI test certificates into Root can trigger a noninteractive prompt
    # on hosted runners, which hangs the smoke test before package install starts.
    $storeNames = @('TrustedPeople')

    foreach ($storeName in $storeNames) {
        if (Test-CertificateInCurrentUserStore -StoreName $storeName -Thumbprint $certificate.Thumbprint) {
            Write-Host "Test signing certificate is already trusted in CurrentUser\$($storeName): $($certificate.Thumbprint)"
            continue
        }

        Write-Host "Trusting test signing certificate in CurrentUser\$($storeName): $($certificate.Thumbprint)"
        Invoke-CertUtil -Arguments @('-user', '-addstore', '-f', $storeName, $CertificatePath)

        $imported += [pscustomobject]@{
            StoreName = $storeName
            Thumbprint = $certificate.Thumbprint
        }
    }

    return $imported
}

function Remove-ImportedCertificates {
    param([object[]]$Certificates)

    foreach ($certificate in $Certificates) {
        try {
            if (Test-CertificateInCurrentUserStore -StoreName $certificate.StoreName -Thumbprint $certificate.Thumbprint) {
                Write-Host "Removing trusted test signing certificate from CurrentUser\$($certificate.StoreName): $($certificate.Thumbprint)"
                Invoke-CertUtil -Arguments @('-user', '-delstore', $certificate.StoreName, $certificate.Thumbprint)
            }
        } catch {
            Write-Warning "Failed to remove trusted test signing certificate '$($certificate.Thumbprint)': $($_.Exception.Message)"
        }
    }
}

function Remove-InstalledPackagesByName {
    param([string]$IdentityName)

    $packages = @(Get-AppxPackage -Name $IdentityName -ErrorAction SilentlyContinue)
    foreach ($package in $packages) {
        Write-Host "Removing installed package before smoke test: $($package.PackageFullName)"
        Remove-AppxPackage -Package $package.PackageFullName -ErrorAction Stop
    }
}

function Install-MsixPackage {
    param(
        [string]$PackagePath,
        [string[]]$DependencyPaths
    )

    $parameters = @{
        Path = $PackagePath
        ForceApplicationShutdown = $true
        ErrorAction = 'Stop'
    }

    if ($DependencyPaths.Count -gt 0) {
        $parameters.DependencyPath = $DependencyPaths
    }

    Write-Host "Installing MSIX package: $PackagePath"
    if ($DependencyPaths.Count -gt 0) {
        Write-Host "Installing with package dependencies:"
        foreach ($dependencyPath in $DependencyPaths) {
            Write-Host "  $dependencyPath"
        }
    }

    Add-AppxPackage @parameters
}

function Get-InstalledPackageByName {
    param([string]$IdentityName)

    $packages = @(Get-AppxPackage -Name $IdentityName -ErrorAction SilentlyContinue)
    if ($packages.Count -eq 0) {
        throw "Package '$IdentityName' was not installed for the current user."
    }

    return $packages[0]
}

function Start-PackagedApplication {
    param([string]$ApplicationUserModelId)

    Add-ApplicationActivationManagerType

    $activationManager = [BO2.PackageSmoke.IApplicationActivationManager](
        New-Object BO2.PackageSmoke.ApplicationActivationManager)
    $processId = 0
    $hr = $activationManager.ActivateApplication(
        $ApplicationUserModelId,
        '',
        [BO2.PackageSmoke.ActivateOptions]::NoErrorUI,
        [ref]$processId)

    if ($hr -ne 0) {
        [System.Runtime.InteropServices.Marshal]::ThrowExceptionForHR($hr)
    }

    if ($processId -le 0) {
        throw "Packaged app activation for '$ApplicationUserModelId' did not return a process id."
    }

    return $processId
}

function New-PropertyCondition {
    param(
        [System.Windows.Automation.AutomationProperty]$Property,
        [object]$Value
    )

    return [System.Windows.Automation.PropertyCondition]::new($Property, $Value)
}

function Find-WindowByProcess {
    param([int]$ProcessId)

    $nameCondition = New-PropertyCondition `
        -Property ([System.Windows.Automation.AutomationElement]::NameProperty) `
        -Value 'BO2'
    $windowCondition = New-PropertyCondition `
        -Property ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) `
        -Value ([System.Windows.Automation.ControlType]::Window)
    $nameAndWindowCondition = [System.Windows.Automation.AndCondition]::new($nameCondition, $windowCondition)

    if ($ProcessId -gt 0) {
        $processCondition = New-PropertyCondition `
            -Property ([System.Windows.Automation.AutomationElement]::ProcessIdProperty) `
            -Value $ProcessId
        $condition = [System.Windows.Automation.AndCondition]::new($nameAndWindowCondition, $processCondition)
    } else {
        $condition = $nameAndWindowCondition
    }

    return [System.Windows.Automation.AutomationElement]::RootElement.FindFirst(
        [System.Windows.Automation.TreeScope]::Children,
        $condition)
}

function Wait-ForTopLevelWindow {
    param(
        [int]$ProcessId,
        [int]$TimeoutSeconds
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    do {
        $window = Find-WindowByProcess -ProcessId $ProcessId
        if ($null -eq $window) {
            $window = Find-WindowByProcess -ProcessId 0
        }

        if ($null -ne $window) {
            $rectangle = $window.Current.BoundingRectangle
            if ($rectangle.Width -gt 0 -and $rectangle.Height -gt 0) {
                return $window
            }
        }

        Start-Sleep -Milliseconds 500
    } while ((Get-Date) -lt $deadline)

    throw "Timed out after $TimeoutSeconds seconds waiting for the BO2 top-level window."
}

function Find-DescendantByName {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType = $null
    )

    $nameCondition = New-PropertyCondition `
        -Property ([System.Windows.Automation.AutomationElement]::NameProperty) `
        -Value $Name

    if ($null -ne $ControlType) {
        $controlTypeCondition = New-PropertyCondition `
            -Property ([System.Windows.Automation.AutomationElement]::ControlTypeProperty) `
            -Value $ControlType
        $condition = [System.Windows.Automation.AndCondition]::new($nameCondition, $controlTypeCondition)
    } else {
        $condition = $nameCondition
    }

    return $Root.FindFirst([System.Windows.Automation.TreeScope]::Subtree, $condition)
}

function Assert-NamedElement {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name,
        [System.Windows.Automation.ControlType]$ControlType = $null
    )

    $element = Find-DescendantByName -Root $Root -Name $Name -ControlType $ControlType
    if ($null -eq $element) {
        if ($null -eq $ControlType) {
            throw "Could not find UI Automation element named '$Name'."
        }

        $controlTypeName = $ControlType.ProgrammaticName
        throw "Could not find UI Automation element named '$Name' with control type '$controlTypeName'."
    }

    return $element
}

function Assert-DisabledButton {
    param(
        [System.Windows.Automation.AutomationElement]$Root,
        [string]$Name
    )

    $button = Assert-NamedElement `
        -Root $Root `
        -Name $Name `
        -ControlType ([System.Windows.Automation.ControlType]::Button)

    if ($button.Current.IsEnabled) {
        throw "Expected button '$Name' to be disabled in the no-game startup state."
    }
}

function Assert-StartupState {
    param([System.Windows.Automation.AutomationElement]$Window)

    Assert-NamedElement -Root $Window -Name 'Current Game Page' | Out-Null
    Assert-NamedElement -Root $Window -Name 'Not connected. Current game stats and events are inactive.' | Out-Null
    Assert-NamedElement -Root $Window -Name 'Game connection' | Out-Null
    Assert-NamedElement -Root $Window -Name 'No game detected' | Out-Null
    Assert-NamedElement -Root $Window -Name 'Disconnected' | Out-Null
    Assert-NamedElement -Root $Window -Name 'Game: Not running' | Out-Null
    Assert-NamedElement -Root $Window -Name 'Game link: Not connected' | Out-Null
    Assert-DisabledButton -Root $Window -Name 'Waiting for game'
}

function Write-AutomationElementTree {
    param(
        [System.Windows.Automation.AutomationElement]$Element,
        [System.IO.TextWriter]$Writer,
        [int]$Depth = 0,
        [int]$MaxDepth = 8
    )

    if ($Depth -gt $MaxDepth) {
        return
    }

    try {
        $current = $Element.Current
        $rectangle = $current.BoundingRectangle
        $controlType = $current.ControlType.ProgrammaticName -replace '^ControlType\.', ''
        $line = '{0}{1} Name="{2}" AutomationId="{3}" IsEnabled={4} IsOffscreen={5} Rect="{6},{7},{8},{9}"' -f `
            ('  ' * $Depth),
            $controlType,
            $current.Name,
            $current.AutomationId,
            $current.IsEnabled,
            $current.IsOffscreen,
            $rectangle.X,
            $rectangle.Y,
            $rectangle.Width,
            $rectangle.Height
        $Writer.WriteLine($line)
    } catch {
        $Writer.WriteLine(('{0}<unavailable: {1}>' -f ('  ' * $Depth), $_.Exception.Message))
        return
    }

    if ($Depth -eq $MaxDepth) {
        return
    }

    try {
        $children = $Element.FindAll(
            [System.Windows.Automation.TreeScope]::Children,
            [System.Windows.Automation.Condition]::TrueCondition)
        foreach ($child in $children) {
            Write-AutomationElementTree -Element $child -Writer $Writer -Depth ($Depth + 1) -MaxDepth $MaxDepth
        }
    } catch {
        $Writer.WriteLine(('{0}<children unavailable: {1}>' -f ('  ' * ($Depth + 1)), $_.Exception.Message))
    }
}

function Save-UiTree {
    param(
        [System.Windows.Automation.AutomationElement]$Window,
        [string]$Path
    )

    if ($null -eq $Window) {
        return
    }

    $writer = [System.IO.StreamWriter]::new($Path, $false, [System.Text.Encoding]::UTF8)
    try {
        Write-AutomationElementTree -Element $Window -Writer $writer
    } finally {
        $writer.Dispose()
    }
}

function Save-Screenshot {
    param([string]$Path)

    try {
        Add-Type -AssemblyName System.Drawing
        Add-Type -AssemblyName System.Windows.Forms

        $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
        $bitmap = [System.Drawing.Bitmap]::new($bounds.Width, $bounds.Height)
        try {
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
            } finally {
                $graphics.Dispose()
            }

            $bitmap.Save($Path, [System.Drawing.Imaging.ImageFormat]::Png)
        } finally {
            $bitmap.Dispose()
        }
    } catch {
        "Screenshot capture failed: $($_.Exception.Message)" |
            Out-File -FilePath "$Path.txt" -Encoding utf8
    }
}

function Save-EventLog {
    param(
        [string]$LogName,
        [string]$Path,
        [DateTime]$StartTime
    )

    try {
        $events = @(Get-WinEvent -FilterHashtable @{ LogName = $LogName; StartTime = $StartTime } -MaxEvents 80 -ErrorAction Stop)
        $events |
            Select-Object TimeCreated, Id, LevelDisplayName, ProviderName, Message |
            Format-List |
            Out-File -FilePath $Path -Encoding utf8
    } catch {
        "Could not collect event log '$LogName': $($_.Exception.Message)" |
            Out-File -FilePath $Path -Encoding utf8
    }
}

function Save-AppPackageActivityLog {
    param(
        [string]$Message,
        [string]$Directory
    )

    $activityIds = @(
        [regex]::Matches($Message, '[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}') |
            ForEach-Object { $_.Value } |
            Select-Object -Unique
    )

    foreach ($activityId in $activityIds) {
        try {
            Get-AppPackageLog -ActivityID $activityId |
                Format-List |
                Out-File -FilePath (Join-Path $Directory "appx-package-$activityId.log") -Encoding utf8
        } catch {
            "Could not collect AppX package log for '$activityId': $($_.Exception.Message)" |
                Out-File -FilePath (Join-Path $Directory "appx-package-$activityId.log") -Encoding utf8
        }
    }
}

function Save-SmokeDiagnostics {
    param(
        [string]$Directory,
        [System.Windows.Automation.AutomationElement]$Window,
        [int]$ProcessId,
        [DateTime]$StartTime,
        [string]$FailureMessage
    )

    if (-not [string]::IsNullOrWhiteSpace($FailureMessage)) {
        $FailureMessage | Out-File -FilePath (Join-Path $Directory 'failure.txt') -Encoding utf8
        Save-AppPackageActivityLog -Message $FailureMessage -Directory $Directory
    }

    if ($ProcessId -gt 0) {
        Get-Process -Id $ProcessId -ErrorAction SilentlyContinue |
            Select-Object Id, ProcessName, Path, StartTime, Responding |
            Format-List |
            Out-File -FilePath (Join-Path $Directory 'launched-process.txt') -Encoding utf8
    }

    Save-UiTree -Window $Window -Path (Join-Path $Directory 'ui-tree.txt')
    Save-Screenshot -Path (Join-Path $Directory 'desktop.png')
    Save-EventLog `
        -LogName 'Microsoft-Windows-AppXDeploymentServer/Operational' `
        -Path (Join-Path $Directory 'appx-deployment-events.log') `
        -StartTime $StartTime
    Save-EventLog `
        -LogName 'Microsoft-Windows-AppModel-Runtime/Admin' `
        -Path (Join-Path $Directory 'appmodel-runtime-events.log') `
        -StartTime $StartTime
}

try {
    $resolvedPackageDirectory = Resolve-RequiredDirectory -Path $PackageDirectory
    $diagnosticsRoot = $DiagnosticsDirectory
    New-Item -ItemType Directory -Force -Path $diagnosticsRoot | Out-Null
    $diagnosticsRoot = (Resolve-Path -LiteralPath $diagnosticsRoot).Path

    Start-Transcript -Path (Join-Path $diagnosticsRoot 'package-smoke-transcript.log') -Force | Out-Null
    $transcriptStarted = $true

    $packagePath = Find-BuildPackage -PackageRoot $resolvedPackageDirectory
    $packageManifest = Read-MsixManifest -PackagePath $packagePath
    $packageIdentityName = $packageManifest.IdentityName
    $dependencyPaths = Get-PackageDependencies -PackageRoot $resolvedPackageDirectory -PackagePath $packagePath
    $certificatePath = Find-TestCertificate -PackageRoot $resolvedPackageDirectory

    $packageManifest |
        ConvertTo-Json -Depth 4 |
        Out-File -FilePath (Join-Path $diagnosticsRoot 'package-manifest.json') -Encoding utf8

    if ($null -ne $certificatePath) {
        $importedCertificates = Import-TestCertificate -CertificatePath $certificatePath
    } else {
        Write-Warning "No test signing certificate was found under '$resolvedPackageDirectory'. Package install will rely on existing trust."
    }

    Remove-InstalledPackagesByName -IdentityName $packageIdentityName
    Install-MsixPackage -PackagePath $packagePath -DependencyPaths $dependencyPaths

    $installedPackage = Get-InstalledPackageByName -IdentityName $packageIdentityName
    $applicationUserModelId = "$($installedPackage.PackageFamilyName)!$($packageManifest.ApplicationId)"
    Write-Host "Launching packaged app: $applicationUserModelId"
    $launchedProcessId = Start-PackagedApplication -ApplicationUserModelId $applicationUserModelId
    Write-Host "Packaged app process id: $launchedProcessId"

    $smokeWindow = Wait-ForTopLevelWindow -ProcessId $launchedProcessId -TimeoutSeconds $LaunchTimeoutSeconds
    Save-UiTree -Window $smokeWindow -Path (Join-Path $diagnosticsRoot 'ui-tree.txt')
    Assert-StartupState -Window $smokeWindow

    Write-Host 'Packaged app install and launch smoke test passed.'
} catch {
    $failureMessage = $_ | Out-String
    if ($null -ne $diagnosticsRoot) {
        Save-SmokeDiagnostics `
            -Directory $diagnosticsRoot `
            -Window $smokeWindow `
            -ProcessId $launchedProcessId `
            -StartTime $smokeStartedAt `
            -FailureMessage $failureMessage
    }

    throw
} finally {
    if ($launchedProcessId -gt 0) {
        try {
            Get-Process -Id $launchedProcessId -ErrorAction SilentlyContinue |
                Stop-Process -Force -ErrorAction SilentlyContinue
        } catch {
            Write-Warning "Failed to stop launched BO2 process '$launchedProcessId': $($_.Exception.Message)"
        }
    }

    if ($null -ne $installedPackage) {
        try {
            Write-Host "Removing smoke-test package: $($installedPackage.PackageFullName)"
            Remove-AppxPackage -Package $installedPackage.PackageFullName -ErrorAction Stop
        } catch {
            Write-Warning "Failed to remove smoke-test package '$($installedPackage.PackageFullName)': $($_.Exception.Message)"
        }
    } elseif (-not [string]::IsNullOrWhiteSpace($packageIdentityName)) {
        try {
            Remove-InstalledPackagesByName -IdentityName $packageIdentityName
        } catch {
            Write-Warning "Failed to remove smoke-test package '$packageIdentityName': $($_.Exception.Message)"
        }
    }

    Remove-ImportedCertificates -Certificates $importedCertificates

    if ($transcriptStarted) {
        Stop-Transcript | Out-Null
    }
}
