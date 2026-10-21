# Genere les packs de la campagne d'exemple proposee a la fin de l'accueil (cf. SamplePackService) :
# un .dmpack par langue de l'appli (memes pistes, titres traduits), ecrits dans
# Resonance/Resources/Raw/SamplePack/.
#
# Les pistes viennent de sources/ (licences dans CREDITS.md) et sont reencodees en AAC (.m4a) via
# ffmpeg : seul format lu partout (l'Ogg Opus ne l'est pas sur iOS). Encodees une seule fois puis
# partagees par tous les packs, pour qu'importer l'exemple dans une langue puis dans l'autre
# reutilise les memes fichiers (deduplication par hash a l'import).
#
# Reproduit le format ecrit par ImportExportService (manifest.json signe HMAC-SHA256 + manifest.sig +
# tracks/<SHA256>.<ext>), niveau 2 (une campagne avec ses canaux) : si ce format ou la cle de
# signature change, ce script doit suivre (cf. aussi Deploy/DemoPack/Build-DemoPack.ps1).
#
# Usage : .\Build-SamplePack.ps1 [-OutputDir <dossier>]

param(
    [string]$OutputDir = (Join-Path $PSScriptRoot "..\..\Resonance\Resources\Raw\SamplePack")
)

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.IO.Compression

# Meme cle que ImportExportService.ManifestSignatureKey.
$SignatureKeyHex = "61d46ee3f647bca5824870dc1330e22e9f5201b2b395c17cba19f27e790ef21e"

function Get-Sha256Hex([byte[]]$bytes) {
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($sha.ComputeHash($bytes))).Replace("-", "") }
    finally { $sha.Dispose() }
}

# ── Bibliotheque ─────────────────────────────────────────────
# Category : libelle de la categorie par defaut dans chaque langue (LibCategory* des AppStrings), pour
# que la piste importee tombe dans la categorie existante de l'install.
$trackDefs = @(
    @{ Id = 1; Source = "tavern_ambience.opus"
       Title = @{ fr = "Auberge animée"; en = "Busy inn" }
       Category = @{ fr = "Ambiance"; en = "Ambience" } }
)

# ── Campagne ─────────────────────────────────────────────────
# Canal : TrackId, Volume (0..1), AutoPlay, Loop, FadeIn, FadeOut. "Arrivée" garde sa piste en lecture
# manuelle, pour qu'on decouvre aussi le bouton lecture du mixeur.
function Ch($trackId, $volume, $auto, $loop, $fadeIn, $fadeOut) {
    @{ TrackId = $trackId; Volume = $volume; AutoPlay = $auto; IsLooping = $loop; FadeIn = $fadeIn; FadeOut = $fadeOut }
}

$campaignDef = @{
    Title = @{ fr = "La Taverne du Vieux Blaireau"; en = "The Old Badger Inn" }
    Sessions = @(
        @{ Title = @{ fr = "Chapitre 1 : Une soirée à l'auberge"; en = "Chapter 1: A night at the inn" }; Scenes = @(
            @{ Title = @{ fr = "Arrivée à l'auberge";   en = "Arriving at the inn" }; Channels = @((Ch 1 0.8 $false $true $true $true)) },
            @{ Title = @{ fr = "Salle commune";         en = "Common room" };         Channels = @((Ch 1 1.0 $true $true $true $true)) },
            @{ Title = @{ fr = "Conversation discrète"; en = "Quiet talk" };          Channels = @((Ch 1 0.35 $true $true $true $true)) }
        ) }
    )
}

$languages = @("fr", "en")

# ── Encodage des pistes ──────────────────────────────────────
if (-not (Get-Command ffmpeg -ErrorAction SilentlyContinue)) { throw "ffmpeg introuvable dans le PATH." }

$workDir = Join-Path ([System.IO.Path]::GetTempPath()) ("resonance-samplepack-" + [Guid]::NewGuid())
New-Item -ItemType Directory -Path $workDir | Out-Null
try {
    $encoded = @{}
    foreach ($t in $trackDefs) {
        $source = Join-Path $PSScriptRoot "sources\$($t.Source)"
        $target = Join-Path $workDir "$($t.Id).m4a"
        # -map_metadata -1 + bitexact : sortie identique d'une generation a l'autre (meme hash).
        & ffmpeg -v error -y -i $source -vn -map_metadata -1 -c:a aac -b:a 128k -fflags +bitexact -flags:a +bitexact $target
        if ($LASTEXITCODE -ne 0) { throw "Echec de l'encodage de $source." }

        $duration = [double]::Parse((& ffprobe -v error -show_entries format=duration -of csv=p=0 $target).Trim(), [Globalization.CultureInfo]::InvariantCulture)
        $bytes = [System.IO.File]::ReadAllBytes($target)
        $encoded[$t.Id] = @{ Bytes = $bytes; Hash = (Get-Sha256Hex $bytes); Duration = [math]::Round($duration, 3) }
    }
}
finally {
    Remove-Item $workDir -Recurse -Force
}

$keyBytes = [byte[]](0..($SignatureKeyHex.Length / 2 - 1) | ForEach-Object { [Convert]::ToByte($SignatureKeyHex.Substring($_ * 2, 2), 16) })
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

foreach ($lang in $languages) {
    # ── Manifeste ────────────────────────────────────────────
    # Noms de proprietes en PascalCase exact : System.Text.Json (options par defaut, cote appli) est
    # sensible a la casse a la deserialisation.
    $tracks = New-Object System.Collections.Generic.List[object]
    foreach ($t in $trackDefs) {
        $e = $encoded[$t.Id]
        $tracks.Add([ordered]@{
            Id = $t.Id; Title = $t.Title[$lang]; Category = $t.Category[$lang]; DurationSeconds = $e.Duration
            DefaultVolume = 1.0; Hash = $e.Hash; FileEntry = "tracks/$($e.Hash).m4a"
        })
    }

    $nextId = 1
    $sessions = New-Object System.Collections.Generic.List[object]
    foreach ($s in $campaignDef.Sessions) {
        $scenes = New-Object System.Collections.Generic.List[object]
        foreach ($sc in $s.Scenes) {
            $sceneTracks = New-Object System.Collections.Generic.List[object]
            $position = 0
            foreach ($ch in $sc.Channels) {
                $sceneTracks.Add([ordered]@{
                    TrackId = $ch.TrackId; Volume = $ch.Volume; Position = $position++
                    AutoPlay = $ch.AutoPlay; IsLooping = $ch.IsLooping; FadeIn = $ch.FadeIn; FadeOut = $ch.FadeOut
                })
            }
            $scenes.Add([ordered]@{ Id = $nextId++; Title = $sc.Title[$lang]; SceneTracks = $sceneTracks.ToArray() })
        }
        $sessions.Add([ordered]@{ Id = $nextId++; Title = $s.Title[$lang]; Scenes = $scenes.ToArray() })
    }

    $manifest = [ordered]@{
        FormatVersion = 1
        ExportLevel   = 2   # StructureWithChannels
        ExportedAt    = (Get-Date).ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ")
        Campaigns     = @([ordered]@{ Id = $nextId++; Title = $campaignDef.Title[$lang]; Sessions = $sessions.ToArray() })
        Tracks        = $tracks.ToArray()
        Library       = $null
    }

    $manifestJson = ConvertTo-Json -InputObject $manifest -Depth 20 -Compress
    $manifestBytes = (New-Object System.Text.UTF8Encoding($false)).GetBytes($manifestJson)

    $hmac = New-Object System.Security.Cryptography.HMACSHA256 (,$keyBytes)
    $signatureHex = ([BitConverter]::ToString($hmac.ComputeHash($manifestBytes))).Replace("-", "")
    $hmac.Dispose()

    # ── Archive ──────────────────────────────────────────────
    $outputPath = Join-Path $OutputDir "sample-campaign.$lang.dmpack"
    if (Test-Path $outputPath) { Remove-Item $outputPath -Force }
    $fileStream = [System.IO.File]::Open($outputPath, [System.IO.FileMode]::CreateNew)
    $zip = New-Object System.IO.Compression.ZipArchive($fileStream, [System.IO.Compression.ZipArchiveMode]::Create)
    try {
        function Add-Entry($name, [byte[]]$data, $level) {
            $stream = $zip.CreateEntry($name, $level).Open()
            try { $stream.Write($data, 0, $data.Length) } finally { $stream.Dispose() }
        }
        foreach ($t in $tracks) {
            Add-Entry $t.FileEntry $encoded[$t.Id].Bytes ([System.IO.Compression.CompressionLevel]::NoCompression)
        }
        Add-Entry "manifest.json" $manifestBytes ([System.IO.Compression.CompressionLevel]::Optimal)
        Add-Entry "manifest.sig" ([System.Text.Encoding]::ASCII.GetBytes($signatureHex)) ([System.IO.Compression.CompressionLevel]::Optimal)
    }
    finally {
        $zip.Dispose()
        $fileStream.Dispose()
    }

    Write-Host "Pack d'exemple genere : $outputPath ($([math]::Round((Get-Item $outputPath).Length / 1MB, 1)) Mo)"
}
