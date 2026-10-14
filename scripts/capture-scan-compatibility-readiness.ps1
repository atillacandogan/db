param(
    [Parameter(Mandatory = $true)][string]$EnvironmentName,
    [Parameter(Mandatory = $true)][string]$ApiBaseUrl,
    [Parameter(Mandatory = $true)][string]$AdminToken,
    [string]$OutputPath = "docs/architecture/scan-compatibility-observations.jsonl"
)

$ErrorActionPreference = "Stop"
$baseUrl = $ApiBaseUrl.TrimEnd("/")
$headers = @{ Authorization = "Bearer $AdminToken" }
$status = Invoke-RestMethod -Uri "$baseUrl/api/status"
$readiness = Invoke-RestMethod `
    -Uri "$baseUrl/api/scan-checks/compatibility-readiness" `
    -Headers $headers

$observation = [ordered]@{
    observedAtUtc = [DateTimeOffset]::UtcNow.ToString("o")
    environment = $EnvironmentName
    deployment = $status
    readiness = $readiness
}

$observation | ConvertTo-Json -Depth 8 -Compress | Add-Content -Path $OutputPath
Write-Host "Compatibility observation appended to $OutputPath."
