[CmdletBinding()]
param(
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET SDK bulunamadi. Once .NET 10 SDK'yi kurun."
}

$projectPath = Join-Path $PSScriptRoot "../src/DbScanner.AppHost/DbScanner.AppHost.csproj"
$projectPath = (Resolve-Path $projectPath).Path

function New-RandomBytes([int]$Length) {
    $bytes = New-Object byte[] $Length
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $generator.GetBytes($bytes)
    }
    finally {
        $generator.Dispose()
    }

    return ,$bytes
}

function New-DemoPassword {
    # Base64 already supplies mixed characters; the suffix guarantees the
    # SQL Server upper/lower/digit/symbol complexity requirements.
    return [Convert]::ToBase64String((New-RandomBytes 24)) + "Aa1!"
}

function New-ProtectionKey {
    return [Convert]::ToBase64String((New-RandomBytes 32))
}

$jsonOutput = (& dotnet user-secrets list --json --project $projectPath | Out-String)
if ($LASTEXITCODE -ne 0) {
    throw "Mevcut User Secrets degerleri okunamadi."
}

$jsonMatch = [regex]::Match($jsonOutput, '(?s)//BEGIN\s*(.*?)\s*//END')
if (-not $jsonMatch.Success) {
    throw "dotnet user-secrets beklenmeyen bir cikti uretti."
}

$existingSecrets = $jsonMatch.Groups[1].Value | ConvertFrom-Json
$existingNames = @($existingSecrets.PSObject.Properties.Name)

$secrets = [ordered]@{
    "Parameters:keycloak-admin-password"          = New-DemoPassword
    "Parameters:dbscanner-initial-admin-password" = New-DemoPassword
    "Parameters:keycloak-admin-service-client-secret" = New-ProtectionKey
    "Parameters:demo-postgres-password"           = New-DemoPassword
    "Parameters:demo-mysql-password"              = New-DemoPassword
    "Parameters:demo-mysql-secure-password"       = New-DemoPassword
    "Parameters:demo-mssql-password"              = New-DemoPassword
    "Parameters:demo-mssql-app-password"          = New-DemoPassword
    "Parameters:demo-mssql-scanner-password"      = New-DemoPassword
    "Parameters:scanner-credential-protection-key" = New-ProtectionKey
    "Parameters:demo-oracle-password"             = New-DemoPassword
    "Parameters:demo-oracle-scanner-password"     = New-DemoPassword
}

$created = 0
$preserved = 0

foreach ($entry in $secrets.GetEnumerator()) {
    if (-not $Force -and $existingNames -contains $entry.Key) {
        $preserved++
        continue
    }

    & dotnet user-secrets --project $projectPath set $entry.Key $entry.Value | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "User Secret ayarlanamadi: $($entry.Key)"
    }

    $created++
}

Write-Host "Gelistirme secret kurulumu tamamlandi. Olusturulan: $created, korunan: $preserved."
Write-Host "Degerler ekrana veya repository'ye yazilmadi."
