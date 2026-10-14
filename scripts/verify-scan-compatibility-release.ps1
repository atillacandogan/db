param(
    [Parameter(Mandatory = $true)]
    [string]$EvidenceBundleDirectory,

    [Parameter(Mandatory = $true)]
    [string]$SigningKeyFile,

    [string]$VerificationOutput
)

$ErrorActionPreference = "Stop"
$manager = Join-Path $PSScriptRoot "manage-scan-compatibility-window.py"
$arguments = @(
    $manager,
    "verify-bundle",
    "--bundle", $EvidenceBundleDirectory,
    "--signing-key-file", $SigningKeyFile
)

if ($VerificationOutput) {
    $arguments += @("--output", $VerificationOutput)
}

& python @arguments
exit $LASTEXITCODE
