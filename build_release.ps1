<#
    build_release.ps1 - PortalMod

    Arma Release/PortalMod/ con solo los archivos necesarios para
    distribuir el mod (listos para comprimir y compartir):
      - ModInfo.xml
      - PortalMod.dll
      - Config/ (carpeta completa)
      - Resources/ (carpeta completa)
      - UIAtlases/ (carpeta completa)
      - README.md

    No compila el proyecto — PortalMod.dll debe existir de antemano
    (compilar Harmony/PortalMod.csproj localmente primero).
#>

$ErrorActionPreference = 'Stop'

$repoRoot = $PSScriptRoot
$outputDir = Join-Path $repoRoot 'Release\PortalMod'

$dllPath = Join-Path $repoRoot 'PortalMod.dll'
if (-not (Test-Path $dllPath)) {
    Write-Error "No se encontro PortalMod.dll en '$repoRoot'. Compila el proyecto (Harmony/PortalMod.csproj) antes de correr este script."
}

if (Test-Path $outputDir) {
    Remove-Item -Recurse -Force $outputDir
}
New-Item -ItemType Directory -Path $outputDir | Out-Null

$filesToCopy = @('ModInfo.xml', 'PortalMod.dll', 'README.md')
foreach ($file in $filesToCopy) {
    $source = Join-Path $repoRoot $file
    if (-not (Test-Path $source)) {
        Write-Error "Falta '$file' en '$repoRoot'."
    }
    Copy-Item -Path $source -Destination $outputDir -Force
}

$foldersToCopy = @('Config', 'Resources', 'UIAtlases')
foreach ($folder in $foldersToCopy) {
    $source = Join-Path $repoRoot $folder
    if (-not (Test-Path $source)) {
        Write-Error "Falta la carpeta '$folder' en '$repoRoot'."
    }
    Copy-Item -Path $source -Destination $outputDir -Recurse -Force
}

Write-Host "Release lista en: $outputDir"
Write-Host "Comprimi esa carpeta (zip) para compartirla."
