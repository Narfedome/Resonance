<#
.SYNOPSIS
    Publie Resonance (Windows + Android) et genere l'installeur Inno Setup, sans rien publier
    (pas de GitHub Release, pas de redeploiement Netlify) : uniquement pour tester une build en
    local avant une vraie release.

.DESCRIPTION
    Meme logique de build que Build-Release.ps1 (meme numero de version, meme keystore de
    signature Android, memes artefacts sous Website\downloads\), mais s'arrete la : pas de
    `gh release create`, pas d'appel au Build Hook Netlify. A utiliser pour installer l'APK sur
    un appareil/emulateur et faire des tests d'integration sans toucher au site public ni
    consommer de credits de build Netlify.

    La version suit le meme schema que la cible SetVersionFromGit du csproj :
    AppVersionMajor.AppVersionMinor.<nombre de commits git> - lu ici depuis le csproj pour
    ne jamais s'en ecarter silencieusement.
    Les deux artefacts finissent sous Website\downloads\ (a la racine du depot), avec un nom de
    fichier fixe (ResonanceInstaller.exe / Resonance.apk) - meme dossier que Build-Release.ps1, donc
    une vraie release ecrasera ces fichiers de test au prochain lancement.
    La partie Android genere aussi l'AAB du Play Store sous Deploy\PlayStore\Resonance-<version>.aab
    (meme fichier que Build-Release.ps1), pour pouvoir l'uploader dans la Play Console sans passer
    par une vraie release. La signature Android
    vient de la keystore de release partagee si Deploy\dmtools-release.keystore est present
    (recupere depuis le Drive partage, jamais commite) et que Deploy\Build-Release.local.ps1
    definit ses identifiants ; sinon elle retombe sur le debug.keystore local, qui differe d'un
    PC a l'autre (sans consequence ici puisque rien n'est publie).

.PARAMETER SkipWindows
    N'effectue que la publication Android (APK + AAB).

.PARAMETER SkipAndroid
    N'effectue que la publication Windows + installeur.

.EXAMPLE
    .\Build-Test.ps1
    Genere l'installeur Windows, l'APK Android ET l'AAB du Play Store, sans rien publier.

.EXAMPLE
    .\Build-Test.ps1 -SkipWindows
    Ne genere que l'APK Android (pour l'installer sur un appareil de test) et l'AAB.
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
    # Meme dossier de sortie que Build-Release.ps1 : index.html (FR/EN) y pointe deja, pratique
    # pour tester le site en local (Website\serve.ps1) avec une vraie build.
    $outputDir        = Join-Path $repoRoot "Website\downloads"
    $outputDirWindows = $outputDir
    $outputDirAndroid = $outputDir
    # AAB du Play Store : meme dossier (gitignore) que Build-Release.ps1.
    $outputDirPlayStore = Join-Path $releaseDir "PlayStore"

    # MSIX du Microsoft Store : meme principe que l'AAB, dossier gitignore.
    $outputDirMicrosoftStore = Join-Path $releaseDir "MicrosoftStore"

    New-Item -ItemType Directory -Force -Path $outputDir, $outputDirPlayStore, $outputDirMicrosoftStore | Out-Null

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

    Write-Host "Version (test) : $version" -ForegroundColor Cyan

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

        # --- MSIX pour le Microsoft Store : non signe (Partner Center le signe a la publication),
        #     x64. Publication separee de l'installeur ci-dessus (WindowsPackageType=MSIX au lieu de
        #     None). L'identite du package (Identity Name/Publisher, PublisherDisplayName) doit etre
        #     celle reservee dans Partner Center, sinon le MSIX est refuse a l'upload. ---
        Write-Host "=== Windows : MSIX (Microsoft Store) ===" -ForegroundColor Cyan
        $msixBuildDir = Join-Path $outputDirMicrosoftStore "build"
        Remove-Item $msixBuildDir -Recurse -Force -ErrorAction SilentlyContinue
        dotnet publish $csprojPath -f net10.0-windows10.0.19041.0 -c Release -p:WindowsPackageType=MSIX -p:RuntimeIdentifierOverride=win-x64 -p:GenerateAppxPackageOnBuild=true -p:AppxPackageSigningEnabled=false "-p:AppxPackageDir=$msixBuildDir/"
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish (Windows, MSIX) a echoue (code $LASTEXITCODE)." }

        $builtMsix = Get-ChildItem $msixBuildDir -Recurse -Filter "*_x64.msix" | Where-Object { $_.FullName -notlike "*\Dependencies\*" } | Select-Object -First 1
        if (-not $builtMsix) { throw "MSIX introuvable sous '$msixBuildDir'." }
        $msixPath = Join-Path $outputDirMicrosoftStore "Resonance-$version.msix"
        Copy-Item -Path $builtMsix.FullName -Destination $msixPath -Force
        Remove-Item $msixBuildDir -Recurse -Force -ErrorAction SilentlyContinue

        Write-Host "MSIX : $msixPath (a uploader dans Partner Center)" -ForegroundColor Green
        [xml]$appxManifest = Get-Content (Join-Path $repoRoot "Resonance\Platforms\Windows\Package.appxmanifest")
        if ($appxManifest.Package.Identity.Publisher -eq "CN=User Name") {
            Write-Warning "Package.appxmanifest a encore l'identite par defaut (Publisher=CN=User Name) : renseigner celle reservee dans Partner Center avant d'uploader le MSIX."
        }
    }

    # --- Android : publish. Le csproj gere le format (AndroidPackageFormat=apk force en config
    #     Release) ; la signature vient de la keystore de release partagee, attendue sous
    #     Deploy\dmtools-release.keystore (gitignoree - cf. .gitignore) : recuperer le fichier
    #     depuis le Drive partage et le deposer dans ce dossier suffit, aucun chemin a configurer.
    #     Sans ce fichier, dotnet publish retombe sur le debug.keystore auto-genere par machine, ce
    #     qui produit une signature differente d'un PC a l'autre - sans consequence pour un test
    #     local (contrairement a une vraie release distribuee). ---
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
            Write-Warning "Deploy\dmtools-release.keystore absent (ou identifiants manquants dans Build-Release.local.ps1) : signature avec le debug.keystore local."
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

        # AAB pour la Play Console : une seconde passe, dotnet publish ne sortant qu'un format a la
        # fois. Une cle de debug envoyee lors du premier upload deviendrait la cle d'upload du Play
        # Store : on le signale bien en evidence plutot que de bloquer le test.
        Write-Host "`n=== Android : AAB (Play Store) ===" -ForegroundColor Cyan
        dotnet publish $csprojPath -f net10.0-android -c Release -p:AndroidPackageFormat=aab @signingArgs
        if ($LASTEXITCODE -ne 0) { throw "dotnet publish (Android, AAB) a echoue (code $LASTEXITCODE)." }

        $publishedAab = Join-Path $repoRoot "Resonance\bin\Release\net10.0-android\publish\com.narfedome.resonance-Signed.aab"
        if (-not (Test-Path $publishedAab)) { throw "AAB signe introuvable a '$publishedAab'." }
        $aabPath = Join-Path $outputDirPlayStore "Resonance-$version.aab"
        Copy-Item -Path $publishedAab -Destination $aabPath -Force

        Write-Host "AAB : $aabPath" -ForegroundColor Green
        if ($signingArgs.Count -eq 0) {
            Write-Warning "AAB signe avec le debug.keystore local : NE PAS l'uploader dans la Play Console (il faut Deploy\dmtools-release.keystore et ses identifiants)."
        }
    }

    Write-Host "`nBuild de test terminee (rien publie : pas de GitHub Release, pas de redeploiement Netlify)." -ForegroundColor Green
}
catch {
    Write-Host "`nECHEC : $($_.Exception.Message)" -ForegroundColor Red
    Read-Host "`nAppuie sur Entree pour fermer"
    exit 1
}
