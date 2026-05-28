param(
    [string]$PublicKeyPath = "C:\path\to\public.pem",
    [string]$EnvVarName = "Updater__ManifestPublicKey"
)

if (-not (Test-Path $PublicKeyPath)) {
    Write-Error "Public key file not found: $PublicKeyPath"
    exit 1
}

# Read the PEM file as raw text preserving newlines
$pem = Get-Content -Raw -Path $PublicKeyPath

# Set environment variable for current process
[System.Environment]::SetEnvironmentVariable($EnvVarName, $pem, [System.EnvironmentVariableTarget]::Process)
Write-Output "Environment variable $EnvVarName set for current process."

# Optionally set for user or machine (commented out for safety)
# [System.Environment]::SetEnvironmentVariable($EnvVarName, $pem, [System.EnvironmentVariableTarget]::User)
# Write-Output "Environment variable $EnvVarName set for current user."

# If you want to persist for the machine (requires admin):
# [System.Environment]::SetEnvironmentVariable($EnvVarName, $pem, [System.EnvironmentVariableTarget]::Machine)
# Write-Output "Environment variable $EnvVarName set for the machine."
