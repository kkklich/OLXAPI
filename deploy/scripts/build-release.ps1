<#
.SYNOPSIS
    Builds deployable artifacts for the Real Estate App.

.DESCRIPTION
    Publishes the .NET API and builds the Angular frontend, then packs both into
    tarballs under artifacts\ ready for deploy-vps.ps1.

    Default output (SSR / Option A):
        artifacts\api.tar.gz    dotnet publish output
        artifacts\web.tar.gz    browser\ + server\  (Angular SSR bundle)

    With -Static (Option B, cyberFolks shared hosting):
        artifacts\web-static.zip   contents of browser\ plus .htaccess,
                                   ready to unpack into public_html\realestate\

.PARAMETER BaseHref
    Angular base href. Use "/" when serving from the domain root (Option A),
    "/realestate/" when serving from a sub-path (Option B default).

.PARAMETER Static
    Build the pre-rendered static bundle instead of the SSR server bundle.
    Implies a default BaseHref of /realestate/.

.PARAMETER Clean
    Run "npm ci" and delete dist\ before building. Slower, but reproducible.

.EXAMPLE
    .\deploy\scripts\build-release.ps1
    SSR build for the domain root.

.EXAMPLE
    .\deploy\scripts\build-release.ps1 -Static
    Static build for cyberFolks under /realestate/.
#>
[CmdletBinding()]
param(
    [string] $BaseHref,
    [switch] $Static,
    [switch] $Clean,
    [switch] $SkipApi,
    [switch] $SkipWeb,
    [string] $WebDir
)

$ErrorActionPreference = 'Stop'

# deploy/ lives inside the API repository, so $RepoRoot is the OLXAPI checkout.
$RepoRoot  = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$ApiDir    = Join-Path $RepoRoot 'AF_mobile_web_api'
$Artifacts = Join-Path $RepoRoot 'artifacts'

# The frontend is a SEPARATE repository (github.com/kkklich/real-estate-app).
# Default to a checkout sitting beside this one, which is the usual layout;
# -WebDir points anywhere else.
if (-not $WebDir) {
    $WebDir = Join-Path (Split-Path -Parent $RepoRoot) 'real-estate-app'
}

if (-not $SkipWeb -and -not (Test-Path (Join-Path $WebDir 'angular.json'))) {
    throw "No Angular project at $WebDir. Clone github.com/kkklich/real-estate-app " +
          "next to this repository, pass -WebDir <path>, or use -SkipWeb to build the API alone."
}

if (-not $BaseHref) {
    if ($Static) { $BaseHref = '/realestate/' } else { $BaseHref = '/' }
}

function Write-Step($msg) { Write-Host "`n==> $msg" -ForegroundColor Cyan }
function Write-Ok($msg)   { Write-Host "[ok] $msg"  -ForegroundColor Green }

function Assert-LastExit($what) {
    if ($LASTEXITCODE -ne 0) { throw "$what failed with exit code $LASTEXITCODE" }
}

# tar.exe ships with Windows 10 1803+ and produces gzip tarballs Linux reads fine.
if (-not (Get-Command tar -ErrorAction SilentlyContinue)) {
    throw "tar.exe not found. Requires Windows 10 1803 or newer."
}

if (-not (Test-Path $Artifacts)) { New-Item -ItemType Directory -Path $Artifacts | Out-Null }

Write-Host "Repo      : $RepoRoot"
Write-Host "Artifacts : $Artifacts"
Write-Host "Base href : $BaseHref"
if ($Static) { Write-Host "Mode      : STATIC (pre-rendered, no Node server)" }
else         { Write-Host "Mode      : SSR (Node server required)" }

# ------------------------------------------------------------------- API ---
if (-not $SkipApi) {
    Write-Step "Publishing .NET API"

    $apiOut = Join-Path $Artifacts 'api'
    if (Test-Path $apiOut) { Remove-Item -Recurse -Force $apiOut }

    Push-Location $ApiDir
    try {
        dotnet publish -c Release -o $apiOut --nologo
        Assert-LastExit 'dotnet publish'
    }
    finally { Pop-Location }

    # Every appsettings*.json is copied into the publish output, and both the
    # Development and Production ones still hold plaintext connection strings.
    # The server reads its real config from /etc/realestate/api.env (env vars
    # beat JSON), so those credentials are pure leak surface on the server.
    $devJson = Join-Path $apiOut 'appsettings.Development.json'
    if (Test-Path $devJson) {
        # Never loaded under ASPNETCORE_ENVIRONMENT=Production anyway.
        Remove-Item -Force $devJson
        Write-Host "[warn] dropped appsettings.Development.json from the artifact" -ForegroundColor Yellow
    }

    $leaked = $false
    foreach ($json in Get-ChildItem $apiOut -Filter 'appsettings*.json') {
        $text = Get-Content $json.FullName -Raw
        if ($text -match 'password\s*=\s*[^";\s]') {
            $leaked = $true
            Write-Host "[warn] stripping credentials from $($json.Name) in the artifact" -ForegroundColor Yellow
            $text = $text -replace '("ConnectionString"\s*:\s*)"[^"]*"', '$1""'
            Set-Content -Path $json.FullName -Value $text -Encoding utf8
        }
    }
    if ($leaked) {
        Write-Host "       The SOURCE files still contain them, and git history still has" -ForegroundColor Yellow
        Write-Host "       the old password. See DEPLOYMENT.md section 3.1." -ForegroundColor Yellow
    }

    $apiTar = Join-Path $Artifacts 'api.tar.gz'
    if (Test-Path $apiTar) { Remove-Item -Force $apiTar }
    tar -czf $apiTar -C $apiOut .
    Assert-LastExit 'tar (api)'

    Write-Ok "artifacts\api.tar.gz  ($([math]::Round((Get-Item $apiTar).Length / 1MB, 1)) MB)"
}

# ------------------------------------------------------------------- WEB ---
if (-not $SkipWeb) {
    Write-Step "Building Angular frontend"

    Push-Location $WebDir
    try {
        if ($Clean) {
            Write-Host "npm ci ..."
            npm ci
            Assert-LastExit 'npm ci'
            if (Test-Path 'dist') { Remove-Item -Recurse -Force 'dist' }
        }
        elseif (-not (Test-Path 'node_modules')) {
            Write-Host "node_modules missing - running npm ci ..."
            npm ci
            Assert-LastExit 'npm ci'
        }

        # The Angular build runs out of heap on this project with parallel
        # workers; one worker is slower but survives.
        $env:NG_BUILD_MAX_WORKERS = '1'

        if ($Static) { $config = 'production-static' } else { $config = 'production' }

        npx ng build --configuration $config --base-href $BaseHref
        Assert-LastExit 'ng build'
    }
    finally { Pop-Location }

    $distRoot = Join-Path $WebDir 'dist\real-estate-app'
    if (-not (Test-Path $distRoot)) { throw "Build output not found at $distRoot" }

    if ($Static) {
        $browserDir = Join-Path $distRoot 'browser'
        $htaccess   = Join-Path $RepoRoot 'deploy\shared-hosting\.htaccess'
        if (Test-Path $htaccess) { Copy-Item $htaccess $browserDir -Force }

        $zip = Join-Path $Artifacts 'web-static.zip'
        if (Test-Path $zip) { Remove-Item -Force $zip }
        Compress-Archive -Path (Join-Path $browserDir '*') -DestinationPath $zip -Force

        Write-Ok "artifacts\web-static.zip  ($([math]::Round((Get-Item $zip).Length / 1MB, 1)) MB)"
        Write-Host "     Unpack its CONTENTS into public_html\realestate\ on cyberFolks."
    }
    else {
        $serverEntry = Join-Path $distRoot 'server\server.mjs'
        if (-not (Test-Path $serverEntry)) {
            throw "server\server.mjs missing - was angular.json's outputMode changed away from 'server'?"
        }

        $webTar = Join-Path $Artifacts 'web.tar.gz'
        if (Test-Path $webTar) { Remove-Item -Force $webTar }
        # browser/ and server/ must sit at the tarball root: server.mjs resolves
        # its static folder as "../browser".
        tar -czf $webTar -C $distRoot browser server
        Assert-LastExit 'tar (web)'

        Write-Ok "artifacts\web.tar.gz  ($([math]::Round((Get-Item $webTar).Length / 1MB, 1)) MB)"
    }
}

Write-Step "Done"
Get-ChildItem $Artifacts -File | Select-Object Name, @{n='Size';e={"{0:N1} MB" -f ($_.Length / 1MB)}}, LastWriteTime | Format-Table -AutoSize

if (-not $Static) {
    Write-Host "Next:  .\deploy\scripts\deploy-vps.ps1 -VpsHost root@YOUR_VPS_IP" -ForegroundColor Cyan
}
