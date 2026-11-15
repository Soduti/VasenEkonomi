#requires -Version 5.1
<#
.SYNOPSIS
Publicerar Vasen till Release och Current.
.EXAMPLE
.\Publish-ToNetworkShare.ps1
.EXAMPLE
.\Publish-ToNetworkShare.ps1 -OfflineOnly
.EXAMPLE
.\Publish-ToNetworkShare.ps1 -KeepOffline
.EXAMPLE
.\Publish-ToNetworkShare.ps1 -OnlineOnly
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$ShareRoot = 'Y:\',
    [string]$LocalPublishDir = (Join-Path $PSScriptRoot 'artifacts\publish'),
    [switch]$SkipPublish,
    [switch]$KeepOffline,
    [switch]$OfflineOnly,
    [switch]$OnlineOnly,
    [ValidateRange(1, 300)]
    [int]$OfflineWaitSeconds = 10
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($OfflineOnly -and $OnlineOnly) {
    throw 'OfflineOnly och OnlineOnly kan inte användas samtidigt.'
}
if (($OfflineOnly -or $OnlineOnly) -and ($KeepOffline -or $SkipPublish)) {
    throw 'OfflineOnly/OnlineOnly ska köras utan KeepOffline eller SkipPublish.'
}

# Kräv en befintlig serverrot så att en frånkopplad enhet/felskriven sökväg upptäcks.
$networkRoot = (Resolve-Path -LiteralPath $ShareRoot).ProviderPath
$currentDir = Join-Path $networkRoot 'Current'
$offlineFile = Join-Path $currentDir 'app_offline.htm'
if ((Test-Path -LiteralPath $currentDir) -and
    ((Get-Item -LiteralPath $currentDir).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
    throw 'Current är en kataloglänk.'
}

function Set-AppOffline {
    New-Item -ItemType Directory -Path $currentDir -Force | Out-Null
    '<!doctype html><html lang="sv"><meta charset="utf-8"><title>Vasen uppdateras</title><h1>Uppdaterar Vasen, strax tillbaka...</h1></html>' |
        Set-Content -LiteralPath $offlineFile -Encoding UTF8
    Write-Host "Offline: $offlineFile"
    # Ge ASP.NET Core Module tid att avsluta processen och släppa filerna.
    Start-Sleep -Seconds $OfflineWaitSeconds
}

function Clear-AppOffline {
    if (Test-Path -LiteralPath $offlineFile) {
        Remove-Item -LiteralPath $offlineFile -Force
    }
    Write-Host "Online: $currentDir"
}

if ($OfflineOnly) { Set-AppOffline; return }
if ($OnlineOnly) { Clear-AppOffline; return }

if (-not $SkipPublish) {
    # Ny lokal mapp för varje byggning: gamla publiceringsfiler följer inte med.
    $LocalPublishDir = Join-Path $PSScriptRoot ('artifacts\network-publish\' + [guid]::NewGuid().ToString('N'))
    $projectPath = Join-Path $PSScriptRoot 'src\Vasen.Web\Vasen.Web.csproj'
    & dotnet publish $projectPath -c $Configuration -o $LocalPublishDir
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish misslyckades (exitkod $LASTEXITCODE). Serverns programfiler har inte ändrats."
    }
}

$sourceDir = (Resolve-Path -LiteralPath $LocalPublishDir).ProviderPath
foreach ($requiredFile in @('Vasen.Web.dll', 'Vasen.Web.deps.json', 'Vasen.Web.runtimeconfig.json', 'web.config')) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDir $requiredFile) -PathType Leaf)) {
        throw "Ofullständig publicering: $requiredFile saknas i $sourceDir."
    }
}
# Tillåt endast programfiler. Databas, bilagor och nycklar får inte publiceras.
$forbidden = @(Get-ChildItem -LiteralPath $sourceDir -Recurse -Force | Where-Object {
    $_.Name -in @('Vasen-data', 'database', 'receipts', 'identity-keys', 'app_offline.htm') -or
    $_.Name -match '\.(db|sqlite|sqlite3)(-wal|-shm)?$' -or $_.Name -like 'key-*.xml' -or
    ($_.Attributes -band [IO.FileAttributes]::ReparsePoint)
})
if ($forbidden.Count -gt 0) {
    throw "Publiceringen innehåller data, offlinefil eller kataloglänkar: $($forbidden.FullName -join ', ')"
}
# Skydda mot att källan ligger i målet (rekursiv kopiering) eller tvärtom.
$sourceFull = [IO.Path]::GetFullPath($sourceDir).TrimEnd('\') + '\'
$targetFull = [IO.Path]::GetFullPath($networkRoot).TrimEnd('\') + '\'
if ($sourceFull.StartsWith($targetFull, [StringComparison]::OrdinalIgnoreCase) -or
    $targetFull.StartsWith($sourceFull, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Publiceringskälla och serverrot får inte överlappa.'
}

$releaseRoot = Join-Path $networkRoot 'Release'
New-Item -ItemType Directory -Path $releaseRoot -Force | Out-Null
$releaseDir = Join-Path $releaseRoot (Get-Date -Format 'yyyy-MM-dd_HH-mm')
# Skriv aldrig över en tidigare release, även vid flera körningar samma minut.
if (Test-Path -LiteralPath $releaseDir) {
    $releaseDir += '_' + [guid]::NewGuid().ToString('N').Substring(0, 8)
}
New-Item -ItemType Directory -Path $releaseDir | Out-Null
$publishFiles = @(Get-ChildItem -LiteralPath $sourceDir -Force)
$publishFiles | Copy-Item -Destination $releaseDir -Recurse -Force
Write-Host "Release: $releaseDir"

Set-AppOffline
try {
    $publishFiles | Copy-Item -Destination $currentDir -Recurse -Force
}
catch {
    Write-Warning "Kopieringen misslyckades. Vasen lämnas offline: $offlineFile. Rätta felet och publicera på nytt innan OnlineOnly används."
    throw
}

if ($KeepOffline) {
    Write-Host "Offline kvar: $offlineFile"
}
else {
    Clear-AppOffline
}
Write-Host "Current: $currentDir"
