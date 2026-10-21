param([switch]$TrustCertificate)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$certDirectory = Join-Path $repoRoot 'certs'
New-Item -ItemType Directory -Force -Path $certDirectory | Out-Null
if ($TrustCertificate) {
    & dotnet dev-certs https --trust
    if ($LASTEXITCODE -ne 0) { throw 'Could not trust the ASP.NET Core development certificate.' }
}
& dotnet dev-certs https --export-path (Join-Path $certDirectory 'localhost.pem') --format Pem --no-password
if ($LASTEXITCODE -ne 0) { throw 'Could not export the HTTPS development certificate.' }
Write-Host 'Certificate exported to certs/localhost.pem and certs/localhost.key.'
Write-Host 'Both files are ignored by Git. Keep the private key local.'
if (-not $TrustCertificate) { Write-Host 'If needed, trust it with: dotnet dev-certs https --trust' }
