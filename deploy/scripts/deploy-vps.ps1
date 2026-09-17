<#
.SYNOPSIS
    Uploads artifacts built by build-release.ps1 to the VPS and activates them.

.DESCRIPTION
    Copies artifacts\*.tar.gz plus release.sh to the server over scp, then runs
    release.sh there. release.sh unpacks into a timestamped release directory,
    flips the "current" symlink, restarts the systemd units, health-checks them,
    and rolls back automatically if either fails to come up.

    Uses the OpenSSH client bundled with Windows 10/11. Set up key-based auth
    first, otherwise you get a password prompt per invocation:
        ssh-keygen -t ed25519
        type $env:USERPROFILE\.ssh\id_ed25519.pub | ssh root@VPS "cat >> ~/.ssh/authorized_keys"

.PARAMETER VpsHost
    user@host of the VPS, e.g. root@203.0.113.10

.PARAMETER Target
    Which side to deploy: api, web, or both (default).

.EXAMPLE
    .\deploy\scripts\deploy-vps.ps1 -VpsHost root@203.0.113.10

.EXAMPLE
    .\deploy\scripts\deploy-vps.ps1 -VpsHost root@203.0.113.10 -Target api
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $VpsHost,

    [ValidateSet('api', 'web', 'both')]
    [string] $Target = 'both',

    [int] $Port = 22
)

$ErrorActionPreference = 'Stop'

$RepoRoot   = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$Artifacts  = Join-Path $RepoRoot 'artifacts'
$ReleaseSh  = Join-Path $PSScriptRoot 'release.sh'
$UploadDir  = '/tmp/realestate-upload'

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Assert-LastExit($what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE" }
}

foreach ($cmd in @('ssh', 'scp')) {
    if (-not (Get-Command $cmd -ErrorAction SilentlyContinue)) {
        throw "$cmd not found. Install the OpenSSH Client optional feature."
    }
}

# Work out which tarballs this run needs, and fail early if they're missing.
$needed = @()
if ($Target -eq 'api' -or $Target -eq 'both') { $needed += 'api.tar.gz' }
if ($Target -eq 'web' -or $Target -eq 'both') { $needed += 'web.tar.gz' }

$toUpload = @()
foreach ($name in $needed) {
    $path = Join-Path $Artifacts $name
    if (-not (Test-Path $path)) {
        throw "$name not found in artifacts\. Run build-release.ps1 first."
    }
    $age = (Get-Date) - (Get-Item $path).LastWriteTime
    if ($age.TotalHours -gt 24) {
        Write-Host "[warn] $name is $([math]::Round($age.TotalHours)) h old - is it the build you meant?" -ForegroundColor Yellow
    }
    $toUpload += $path
}

Write-Host "Host      : $VpsHost (port $Port)"
Write-Host "Target    : $Target"
Write-Host "Uploading : $($needed -join ', ')"

Write-Step "Preparing $UploadDir on the server"
ssh -p $Port $VpsHost "rm -rf $UploadDir && mkdir -p $UploadDir"
Assert-LastExit 'ssh (mkdir)'

Write-Step "Uploading"
foreach ($path in $toUpload) {
    Write-Host "  $(Split-Path -Leaf $path) ..."
    scp -P $Port -q $path "${VpsHost}:$UploadDir/"
    Assert-LastExit "scp $(Split-Path -Leaf $path)"
}
scp -P $Port -q $ReleaseSh "${VpsHost}:$UploadDir/release.sh"
Assert-LastExit 'scp release.sh'

Write-Step "Activating release on the server"
# release.sh restarts the services, health-checks them, and rolls back on
# failure - a non-zero exit here means the previous release is already restored.
ssh -p $Port $VpsHost "bash $UploadDir/release.sh $Target"
Assert-LastExit 'release.sh'

Write-Step "Deployed"
Write-Host "Check it:  curl -I https://YOUR.DOMAIN/" -ForegroundColor Cyan
Write-Host "Logs:      ssh $VpsHost 'journalctl -u realestate-api -u realestate-ssr -f'" -ForegroundColor Cyan
