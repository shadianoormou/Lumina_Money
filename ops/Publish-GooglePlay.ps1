[CmdletBinding()]
param(
    [Parameter(Mandatory)] [ValidatePattern('^https://')] [string] $ApiBaseUrl,
    [Parameter(Mandatory)] [string] $IaphubAppId,
    [Parameter(Mandatory)] [string] $IaphubPublicApiKey,
    [Parameter(Mandatory)] [string] $UploadKeyStore,
    [Parameter(Mandatory)] [string] $UploadKeyAlias,
    [Parameter(Mandatory)] [ValidatePattern('^\d+\.\d+\.\d+$')] [string] $VersionName,
    [Parameter(Mandatory)] [ValidateRange(1, 2100000000)] [int] $VersionCode,
    [string] $OutputDirectory = 'artifacts\google-play'
)

$ErrorActionPreference = 'Stop'
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace 'LuminaMoney.App\LuminaMoney.App.csproj'
$testProject = Join-Path $workspace 'LuminaMoney.Tests\LuminaMoney.Tests.csproj'
$keyStorePath = (Resolve-Path -LiteralPath $UploadKeyStore).Path
$outputPath = [System.IO.Path]::GetFullPath((Join-Path $workspace $OutputDirectory))

if (-not $env:ANDROID_KEYSTORE_PASSWORD -or -not $env:ANDROID_KEY_PASSWORD) {
    throw 'Set ANDROID_KEYSTORE_PASSWORD and ANDROID_KEY_PASSWORD in the current secret environment. Do not pass or commit raw passwords.'
}
if (-not $ApiBaseUrl.EndsWith('/api/v1/')) {
    throw 'ApiBaseUrl must be the production HTTPS endpoint ending in /api/v1/.'
}
if ($IaphubAppId -eq 'not-configured' -or $IaphubPublicApiKey -eq 'not-configured') {
    throw 'Use the real IAPHUB Android public app credentials.'
}

New-Item -ItemType Directory -Force -Path $outputPath | Out-Null

dotnet test $testProject --configuration Release
if ($LASTEXITCODE -ne 0) { throw 'Regression tests failed; Google Play artifact was not created.' }

dotnet publish $project -f net10.0-android -c Release -o $outputPath `
    -p:ProductionApiBaseUrl=$ApiBaseUrl `
    -p:IaphubAppId=$IaphubAppId `
    -p:IaphubApiKey=$IaphubPublicApiKey `
    -p:ApplicationDisplayVersion=$VersionName `
    -p:ApplicationVersion=$VersionCode `
    -p:AndroidKeyStore=true `
    -p:AndroidSigningKeyStore=$keyStorePath `
    -p:AndroidSigningKeyAlias=$UploadKeyAlias `
    -p:AndroidSigningKeyPass=env:ANDROID_KEY_PASSWORD `
    -p:AndroidSigningStorePass=env:ANDROID_KEYSTORE_PASSWORD
if ($LASTEXITCODE -ne 0) { throw 'Signed Android App Bundle publish failed.' }

$bundle = Get-ChildItem -LiteralPath $outputPath -Filter '*-Signed.aab' | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1
if (-not $bundle) { throw 'Publish completed without a signed .aab artifact.' }

$hash = Get-FileHash -LiteralPath $bundle.FullName -Algorithm SHA256
[pscustomobject]@{
    Bundle = $bundle.FullName
    VersionName = $VersionName
    VersionCode = $VersionCode
    Sha256 = $hash.Hash
}
