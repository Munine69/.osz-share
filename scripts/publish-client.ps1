param(
    [string]$Version = (Get-Date -Format "yyyy.MM.dd-HHmm"),
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$projectPath = Join-Path $repoRoot "src/OszShare.Client/OszShare.Client.csproj"
$artifactRoot = Join-Path $repoRoot "artifacts/client/$Runtime"
$publishDir = Join-Path $artifactRoot "publish"
$zipName = "OszShare-$Version-$Runtime.zip"
$zipPath = Join-Path $artifactRoot $zipName
$hashPath = "$zipPath.sha256.txt"
$sourceExeName = "OszShare.Client.exe"
$releaseExeName = ".osz Share.exe"

if (-not (Test-Path $projectPath)) {
    throw "Client project not found: $projectPath"
}

if (Test-Path $artifactRoot) {
    Remove-Item -Recurse -Force $artifactRoot
}

New-Item -ItemType Directory -Path $publishDir -Force | Out-Null

$publishArgs = @(
    "publish", $projectPath,
    "-c", $Configuration,
    "-r", $Runtime,
    "--self-contained", "true",
    "-o", $publishDir,
    "/p:PublishSingleFile=true",
    "/p:IncludeNativeLibrariesForSelfExtract=true",
    "/p:PublishTrimmed=false",
    "/p:DebugType=None",
    "/p:DebugSymbols=false"
)

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

$sourceExePath = Join-Path $publishDir $sourceExeName
$releaseExePath = Join-Path $publishDir $releaseExeName
if (Test-Path $sourceExePath) {
    if (Test-Path $releaseExePath) {
        Remove-Item -Force $releaseExePath
    }

    Move-Item -Path $sourceExePath -Destination $releaseExePath -Force
}

Compress-Archive -Path (Join-Path $publishDir "*") -DestinationPath $zipPath -CompressionLevel Optimal -Force

$hash = (Get-FileHash -Path $zipPath -Algorithm SHA256).Hash
"$hash  $zipName" | Set-Content -Path $hashPath -Encoding ascii

Write-Host ""
Write-Host "Client publish completed."
Write-Host "Publish folder: $publishDir"
Write-Host "Release exe:    $releaseExeName"
Write-Host "Release zip:    $zipPath"
Write-Host "SHA256 file:    $hashPath"
