[CmdletBinding()]
param(
  [Parameter(Mandatory = $true)]
  [string]$ManifestPath,

  [Parameter(Mandatory = $true)]
  [string]$PfxPath,

  [Parameter(Mandatory = $true)]
  [string]$CerPath,

  [string]$FriendlyName = 'BO2 CI Test Signing Certificate'
)

$newSelfSignedCertificateCommand = Get-Command -Name New-SelfSignedCertificate -ErrorAction SilentlyContinue
if ($null -eq $newSelfSignedCertificateCommand) {
  throw 'New-SelfSignedCertificate is required to create an MSIX test signing certificate. Run this script on Windows.'
}

$resolvedManifestPath = (Resolve-Path -LiteralPath $ManifestPath).Path
[xml]$manifest = Get-Content -LiteralPath $resolvedManifestPath -Raw

$namespaceManager = [System.Xml.XmlNamespaceManager]::new($manifest.NameTable)
$namespaceManager.AddNamespace('appx', 'http://schemas.microsoft.com/appx/manifest/foundation/windows10')
$identityNode = $manifest.SelectSingleNode('/appx:Package/appx:Identity', $namespaceManager)

if ($null -eq $identityNode -or [string]::IsNullOrWhiteSpace($identityNode.Publisher)) {
  throw "Package publisher was not found in '$resolvedManifestPath'."
}

$publisher = $identityNode.Publisher

foreach ($path in @($PfxPath, $CerPath)) {
  $directory = Split-Path -Path $path -Parent
  if (-not [string]::IsNullOrWhiteSpace($directory)) {
    New-Item -ItemType Directory -Force -Path $directory | Out-Null
  }
}

$password = [System.Security.SecureString]::new()
$certificate = New-SelfSignedCertificate `
  -Type Custom `
  -Subject $publisher `
  -KeyUsage DigitalSignature `
  -FriendlyName $FriendlyName `
  -CertStoreLocation 'Cert:\CurrentUser\My' `
  -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')

try {
  Export-PfxCertificate -Cert $certificate -FilePath $PfxPath -Password $password | Out-Null
  Export-Certificate -Cert $certificate -FilePath $CerPath | Out-Null
} catch {
  Remove-Item -LiteralPath "Cert:\CurrentUser\My\$($certificate.Thumbprint)" -Force -ErrorAction SilentlyContinue
  throw
}

$certificate.Thumbprint
