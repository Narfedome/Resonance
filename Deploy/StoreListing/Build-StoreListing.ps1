<#
.SYNOPSIS
    Genere les elements graphiques de la fiche Google Play sous Deploy\StoreListing\output\ :
    icone 512x512, image de presentation 1024x500 (FR + EN) et captures telephone en 9:16.

.DESCRIPTION
    - Icone : export PNG de Resonance\Resources\AppIcon\appicon.svg via Inkscape.
    - Image de presentation : rendu de feature-graphic.html (meme charte que l'image Open Graph
      du site) via Chrome headless.
    - Captures : les captures du site (Website\assets\screenshots\, 1080x2400, soit du 20:9 que
      Google Play refuse) sont centrees dans un cadre 1350x2400 (9:16) via screenshot-frame.html,
      avec des bandes laterales de la couleur de fond de l'appli.
    A relancer apres chaque changement d'icone, de charte ou de captures.
#>

$ErrorActionPreference = "Stop"

try {
    $listingDir = $PSScriptRoot
    $repoRoot   = Split-Path -Parent (Split-Path -Parent $listingDir)
    $outputDir  = Join-Path $listingDir "output"
    $inkscape   = "C:\Program Files\Inkscape\bin\inkscape.exe"
    $chrome     = "C:\Program Files\Google\Chrome\Application\chrome.exe"
    $iconSvg    = Join-Path $repoRoot "Resonance\Resources\AppIcon\appicon.svg"
    $shotsDir   = Join-Path $repoRoot "Website\assets\screenshots"
    $background = "1c1a2e"

    foreach ($tool in $inkscape, $chrome) {
        if (-not (Test-Path $tool)) { throw "Outil introuvable : $tool" }
    }
    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

    function Invoke-ChromeScreenshot([string]$url, [int]$width, [int]$height, [string]$output) {
        # Profil jetable par capture : sans lui, Chrome headless se raccroche au Chrome deja ouvert
        # (meme profil) et ne rend jamais la main. --virtual-time-budget laisse le temps aux polices
        # Google Fonts de se charger avant la capture.
        $profileDir = Join-Path $env:TEMP "storelisting-chrome-$([guid]::NewGuid().ToString('N'))"
        $chromeArgs = @(
            "--headless=new", "--disable-gpu", "--hide-scrollbars", "--no-first-run",
            "--no-default-browser-check", "--force-device-scale-factor=1",
            "--user-data-dir=`"$profileDir`"", "--window-size=$width,$height",
            "--virtual-time-budget=8000", "--screenshot=`"$output`"", "`"$url`""
        )
        Remove-Item $output -ErrorAction SilentlyContinue
        $process = Start-Process -FilePath $chrome -ArgumentList $chromeArgs -PassThru -WindowStyle Hidden
        if (-not $process.WaitForExit(60000)) { $process.Kill(); throw "Chrome bloque (60 s) : $output" }
        Remove-Item $profileDir -Recurse -Force -ErrorAction SilentlyContinue
        if (-not (Test-Path $output)) { throw "Capture Chrome echouee : $output" }
        Write-Host "  $output" -ForegroundColor Green
    }

    function ConvertTo-FileUrl([string]$path) {
        return ([System.Uri](Resolve-Path $path).Path).AbsoluteUri
    }

    Write-Host "=== Icone 512x512 ===" -ForegroundColor Cyan
    $iconPng = Join-Path $outputDir "icon-512.png"
    & $inkscape $iconSvg --export-type=png --export-width=512 --export-height=512 "--export-filename=$iconPng" 2>$null | Out-Null
    if (-not (Test-Path $iconPng)) { throw "Export Inkscape de l'icone echoue." }
    Write-Host "  $iconPng" -ForegroundColor Green

    Write-Host "=== Image de presentation 1024x500 ===" -ForegroundColor Cyan
    $featureUrl = ConvertTo-FileUrl (Join-Path $listingDir "feature-graphic.html")
    Invoke-ChromeScreenshot "$featureUrl" 1024 500 (Join-Path $outputDir "feature-graphic-fr.png")
    Invoke-ChromeScreenshot "$featureUrl`?lang=en" 1024 500 (Join-Path $outputDir "feature-graphic-en.png")

    Write-Host "=== Captures telephone 1350x2400 (9:16) ===" -ForegroundColor Cyan
    $frameUrl = ConvertTo-FileUrl (Join-Path $listingDir "screenshot-frame.html")
    foreach ($shot in Get-ChildItem $shotsDir -Filter *.png | Sort-Object Name) {
        $src = [System.Uri]::EscapeDataString((ConvertTo-FileUrl $shot.FullName))
        Invoke-ChromeScreenshot "$frameUrl`?src=$src&bg=$background" 1350 2400 (Join-Path $outputDir "phone-$($shot.BaseName).png")
    }

    Write-Host "`nElements de la fiche Play Store generes dans $outputDir" -ForegroundColor Green
}
catch {
    Write-Host "`nECHEC : $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "`nAppuie sur Entree pour fermer"
    exit 1
}
