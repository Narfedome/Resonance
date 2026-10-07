<#
.SYNOPSIS
    Publie Resonance (Windows + Android), genere l'installeur Inno Setup, et diffuse la release
    (GitHub + redeploiement du site), avec le meme numero de version que celui affiche dans
    l'application (Reglages > version).

.DESCRIPTION
    Tout ce dont a besoin une release vit dans ce dossier Deploy\ (ce script, sa config locale,
    Installer.iss, la keystore de signature Android) plutot que d'etre eparpille a la racine du
    depot et dans Resonance\ : plus simple a retrouver, et surtout ce script se lance tel quel en
    clic droit > Executer avec PowerShell, sans avoir besoin d'ouvrir un terminal pour lui passer
    un parametre - la publication (GitHub Release + redeploiement Netlify) se fait donc toujours,
    dans tous les cas.

    La version suit le meme schema que la cible SetVersionFromGit du csproj :
    AppVersionMajor.AppVersionMinor.<nombre de commits git> - lu ici depuis le csproj pour
    ne jamais s'en ecarter silencieusement.
    Les deux artefacts finissent sous Website\downloads\ (a la racine du depot), avec un nom de
    fichier fixe (ResonanceInstaller.exe / Resonance.apk) : pas de numero de version dans le nom, pour
    que le lien de telechargement du site n'ait jamais besoin de changer. Ce dossier est gitignore
    - les binaires sont plutot attaches a une GitHub Release (v<version>), seule source que
    Website/scripts/fetch-downloads.js va lire a chaque build Netlify du site, puis le Build Hook
    Netlify (Deploy/Build-Release.local.ps1) est appele pour redeployer immediatement. La
    signature Android vient de la keystore de release partagee si Deploy\dmtools-release.keystore
    est present (recupere depuis le Drive partage, jamais commite) et que
    Deploy\Build-Release.local.ps1 definit ses identifiants (a creer une fois par machine depuis
    Build-Release.local.ps1.example) ; sinon elle retombe sur le debug.keystore local, qui differe
    d'un PC a l'autre.

.PARAMETER SkipWindows
    N'effectue que la publication Android.

.PARAMETER SkipAndroid
    N'effectue que la publication Windows + installeur.

.EXAMPLE
    .\Build-Release.ps1
    Genere l'installeur Windows ET l'APK Android, publie une GitHub Release et redeploie le site.
    C'est la commande lancee par un clic droit > Executer avec PowerShell.

.EXAMPLE
    .\Build-Release.ps1 -SkipAndroid
    Ne genere/publie que l'installeur Windows.
#>

param(
    [switch]$SkipWindows,
    [switch]$SkipAndroid
)

$ErrorActionPreference = "Stop"

# Tout le corps du script est dans ce try/catch : lance en double-clic (pas depuis un terminal deja
# ouvert), une erreur non rattrapee fermerait sinon la fenetre PowerShell instantanement avec elle,
# sans laisser le temps de lire le message.
try {
    $releaseDir       = $PSScriptRoot
    $repoRoot         = Split-Path -Parent $releaseDir
    $csprojPath       = Join-Path $repoRoot "Resonance\Resonance.csproj"
    $issPath          = Join-Path $releaseDir "Installer.iss"
    $isccPath         = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
    # Les deux artefacts finissent directement sous Website\downloads\, exactement là où
    # index.html (FR/EN) les référence (downloads/ResonanceInstaller.exe, downloads/Resonance.apk) :
    # un site republié après un build reflète tout de suite la dernière version, sans étape de
    # copie manuelle.
    $outputDir        = Join-Path $repoRoot "Website\downloads"
    $outputDirWindows = $outputDir
    $outputDirAndroid = $outputDir

    New-Item -ItemType Directory -Force -Path $outputDir | Out-Null

    # --- Config locale (jamais commitee, cf. .gitignore) : chemin + mots de passe de la keystore de
    #     release, propres a chaque machine. Copier Build-Release.local.ps1.example -> Build-Release.local.ps1
    #     (dans ce meme dossier Deploy\) et renseigner les valeurs une seule fois par PC. ---
    $localConfigPath = Join-Path $releaseDir "Build-Release.local.ps1"
    if (Test-Path $localConfigPath) {
        . $localConfigPath
    }

    # --- Version : major.minor du csproj + patch = nombre de commits git (identique a la cible
    #     SetVersionFromGit du csproj, pour que l'installeur, l'APK et l'appli affichent le meme numero) ---
    [xml]$csproj = Get-Content $csprojPath
    $major = $csproj.Project.PropertyGroup.AppVersionMajor | Where-Object { $_ } | Select-Object -First 1
    $minor = $csproj.Project.PropertyGroup.AppVersionMinor | Where-Object { $_ } | Select-Object -First 1
    $patch = (git -C $repoRoot rev-list --count HEAD).Trim()
    $version = "$major.$minor.$patch"

    Write-Host "Version : $version" -ForegroundColor Cyan

    # --- Windows : publish (unpackaged, cf. WindowsPackageType=None du csproj) + installeur Inno Setup ---
    if (-not $SkipWindows) {
        if (-not (Test-Path $isccPath)) {
            throw "Inno Setup Compiler introuvable a '$isccPath'. Installe Inno Setup 6 ou ajuste `$isccPath dans ce script."
        }

        Write-Host "`n=== Windows : publication ===" -ForegroundColor Cyan
        dotnet publish $csprojPath -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=None
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish (Windows) a echoue (code $LASTEXITCODE)." }

        Write-Host "=== Windows : compilation de l'installeur ===" -ForegroundColor Cyan
        # /O et /F surchargent OutputDir/OutputBaseFilename du .iss : nom de fichier fixe (sans version)
        # pour que le lien de telechargement public n'ait jamais besoin de changer.
        & $isccPath "/DMyAppVersion=$version" "/O$outputDirWindows" "/FResonanceInstaller" $issPath
        if ($LASTEXITCODE -ne 0) { throw "ISCC a echoue (code $LASTEXITCODE)." }

        Write-Host "Installeur : $outputDirWindows\ResonanceInstaller.exe" -ForegroundColor Green
    }

    # --- Android : publish. Le csproj gere le format (AndroidPackageFormat=apk force en config
    #     Release) ; la signature vient de la keystore de release partagee, attendue sous
    #     Deploy\dmtools-release.keystore (gitignoree - cf. .gitignore) : recuperer le fichier
    #     depuis le Drive partage et le deposer dans ce dossier suffit, aucun chemin a configurer.
    #     Sans ce fichier, dotnet publish retombe sur le debug.keystore auto-genere par machine, ce
    #     qui produit une signature differente d'un PC a l'autre et fait refuser les mises a jour par
    #     Android ("app not installed as package conflicts with an existing package"). ---
    if (-not $SkipAndroid) {
        Write-Host "`n=== Android : publication ===" -ForegroundColor Cyan

        $keystorePath  = Join-Path $releaseDir "dmtools-release.keystore"
        $keystoreAlias = $env:DMTOOLS_KEYSTORE_ALIAS
        $storePass     = $env:DMTOOLS_KEYSTORE_STOREPASS
        $keyPass       = $env:DMTOOLS_KEYSTORE_KEYPASS

        $signingArgs = @()
        if ((Test-Path $keystorePath) -and $keystoreAlias -and $storePass -and $keyPass) {
            Write-Host "Signature : keystore de release ($keystorePath)" -ForegroundColor DarkGray
            $signingArgs = @(
                "-p:AndroidKeyStore=true"
                "-p:AndroidSigningKeyStore=$keystorePath"
                "-p:AndroidSigningKeyAlias=$keystoreAlias"
                "-p:AndroidSigningStorePass=$storePass"
                "-p:AndroidSigningKeyPass=$keyPass"
            )
        } else {
            Write-Warning "Deploy\dmtools-release.keystore absent (ou identifiants manquants dans Build-Release.local.ps1) : signature avec le debug.keystore local (differente d'un PC a l'autre, a eviter pour une release distribuee)."
        }

        dotnet publish $csprojPath -f net10.0-android -c Release @signingArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish (Android) a echoue (code $LASTEXITCODE)." }

        # Copie sous un nom fixe (sans version) a cote de l'installeur Windows : meme raison que
        # pour l'exe, le lien de telechargement public n'a jamais besoin de changer.
        $publishedApk = Join-Path $repoRoot "Resonance\bin\Release\net10.0-android\publish\com.narfedome.resonance-Signed.apk"
        if (-not (Test-Path $publishedApk)) { throw "APK signe introuvable a '$publishedApk'." }
        $apkPath = Join-Path $outputDirAndroid "Resonance.apk"
        Copy-Item -Path $publishedApk -Destination $apkPath -Force

        Write-Host "APK : $apkPath" -ForegroundColor Green
    }

    # --- Publication GitHub Release : seule source des binaires pour le site (jamais commites,
    #     cf. .gitignore) - fetch-downloads.js les recupere depuis /releases/latest a chaque build
    #     Netlify. Toujours executee (pas de switch d'activation) pour qu'un simple clic droit >
    #     Executer avec PowerShell suffise a publier de bout en bout. N'attache que ce qui existe
    #     reellement (utile si -SkipWindows/-SkipAndroid). ---
    Write-Host "`n=== Publication GitHub Release v$version ===" -ForegroundColor Cyan

    if (-not (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw "GitHub CLI (gh) introuvable. Installe-le et authentifie-toi (gh auth login) pour publier une release."
    }

    $assets = @(
        Join-Path $outputDirWindows "ResonanceInstaller.exe"
        Join-Path $outputDirAndroid "Resonance.apk"
    ) | Where-Object { Test-Path $_ }

    if ($assets.Count -eq 0) {
        throw "Aucun artefact a publier (exe/apk introuvables sous $outputDir)."
    }

    gh release create "v$version" @assets --title "v$version" --notes "Build automatise depuis Build-Release.ps1."
    if ($LASTEXITCODE -ne 0) { throw "gh release create a echoue (code $LASTEXITCODE)." }

    Write-Host "Release publiee : v$version ($($assets.Count) fichier(s))" -ForegroundColor Green

    # --- Redeploiement Netlify : un simple POST sur le Build Hook (cf. Site settings > Build &
    #     deploy > Build hooks) suffit a declencher un nouveau build du site, qui va re-executer
    #     fetch-downloads.js et recuperer les binaires qu'on vient de publier. Sans ca, le site
    #     ne se met a jour qu'au prochain push sur master (ou clic manuel dans le dashboard). ---
    $netlifyHookUrl = $env:DMTOOLS_NETLIFY_BUILD_HOOK
    if ($netlifyHookUrl) {
        Write-Host "`n=== Redeploiement du site (Netlify Build Hook) ===" -ForegroundColor Cyan
        try {
            Invoke-RestMethod -Method Post -Uri $netlifyHookUrl -Body (@{ trigger_title = "Build-Release.ps1 v$version" } | ConvertTo-Json) -ContentType "application/json" | Out-Null
            Write-Host "Site redeploye avec les binaires v$version." -ForegroundColor Green
        } catch {
            Write-Warning "Echec de l'appel au Build Hook Netlify : $($_.Exception.Message). Le site se mettra a jour au prochain push sur master, ou via un redeploiement manuel."
        }
    } else {
        Write-Warning "DMTOOLS_NETLIFY_BUILD_HOOK non defini dans Deploy\Build-Release.local.ps1 : le site ne se redeploiera pas tout seul, il faudra pousser sur master ou redeployer a la main depuis Netlify."
    }
}
catch {
    Write-Host "`nECHEC : $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "`nAppuie sur Entree pour fermer"
    exit 1
}
