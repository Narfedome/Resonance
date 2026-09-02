# DM Tools

*Français · [English](README.en.md)*

> Prépare l'ambiance sonore de ta campagne de jeu de rôle sur table, scène après scène.

[![Unit Tests](https://github.com/Narfedome/DmTools/actions/workflows/tests.yml/badge.svg)](https://github.com/Narfedome/DmTools/actions/workflows/tests.yml)

DM Tools est une application communautaire pour les maîtres du jeu : un mixeur audio,
un gestionnaire de campagnes et une bibliothèque de pistes, pensés pour une partie
autour de la table. **100 % hors ligne** — aucune connexion requise, tout est stocké
localement sur l'appareil.

Site : <https://dmtools-app.netlify.app/> · Disponible sur **Windows** et **Android**.

## Fonctionnalités

- **Mixeur audio** — superposition de plusieurs pistes en direct (lecture, boucle,
  fondu d'entrée/sortie et volume indépendants par piste) pour installer une ambiance
  instantanément. Utilisable sur une scène ou en mode libre, sans passer par une scène.
- **Bibliothèque audio** — centralise tes pistes `.mp3`, réutilisables d'une campagne à
  l'autre, regroupées automatiquement par catégorie.
- **Organisation par campagne** — campagnes → chapitres → scènes, avec réorganisation
  par glisser-déposer, pour retrouver la bonne scène en pleine partie.
- **Import / export** — packs `.dmpack` signés (détection d'altération) pour partager
  une campagne ou une bibliothèque entière.
- **Thèmes visuels** et interface **française / anglaise**.
- **Hors ligne** — la base SQLite et les fichiers audio ne quittent jamais l'appareil.

## Captures d'écran

| Mixeur audio | Réglages d'une piste | Organisation par campagne |
|:---:|:---:|:---:|
| <img src="Website/assets/screenshots/1.png" width="240" alt="Mixeur audio" /> | <img src="Website/assets/screenshots/2.png" width="240" alt="Réglages d'une piste" /> | <img src="Website/assets/screenshots/3.png" width="240" alt="Organisation par campagne" /> |
| **Bibliothèque audio** | **Import / export d'une campagne** | **Réglages** |
| <img src="Website/assets/screenshots/4.png" width="240" alt="Bibliothèque audio" /> | <img src="Website/assets/screenshots/5.png" width="240" alt="Import et export d'une campagne" /> | <img src="Website/assets/screenshots/6.png" width="240" alt="Réglages de l'application" /> |

## Stack technique

- [.NET 10](https://dotnet.microsoft.com/) / **.NET MAUI** (`net10.0-android`,
  `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows`)
- MVVM avec [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) et
  [CommunityToolkit.Maui](https://github.com/CommunityToolkit/Maui)
- [Plugin.Maui.Audio](https://github.com/jfversluis/Plugin.Maui.Audio) pour la lecture audio
- [sqlite-net-pcl](https://github.com/praeclarum/sqlite-net) pour la persistance locale
- [TagLibSharp](https://github.com/mono/taglib-sharp) pour les métadonnées et pochettes des pistes
- Tests : [xUnit](https://xunit.net/)

## Structure du dépôt

| Projet / dossier        | Rôle |
|-------------------------|------|
| `DmToolsApp/`           | Application MAUI : pages, vues, composants, services de plateforme, ressources. |
| `DmToolsApp.Core/`      | Logique pure partagée et testable (modèles, accès données SQLite, services d'import/export et de bibliothèque). Aucune dépendance MAUI UI. |
| `DmToolsApp.Tests/`     | Tests unitaires xUnit, ne référencent que `DmToolsApp.Core` (net10.0 pur, sans workload MAUI). |
| `Deploy/`               | Scripts de publication (`Build-Release.ps1`, `Build-Test.ps1`), script Inno Setup (`Installer.iss`), keystore Android (non commitée). |
| `Website/`              | Site vitrine statique (FR/EN), déployé sur Netlify. |
| `.github/workflows/`    | CI : tests unitaires à chaque push/PR, build iOS non signé sur demande. |

> Les `ViewModels` liés à l'UI restent dans l'application MAUI ; `Core` ne contient que
> de la logique partagée réellement indépendante de l'UI.

## Prérequis

- [SDK .NET 10](https://dotnet.microsoft.com/download/dotnet/10.0)
- Workloads MAUI : `dotnet workload install maui`
- Pour builder l'installeur Windows : [Inno Setup 6](https://jrsoftware.org/isinfo.php)
- Visual Studio 2022+ ou VS Code avec l'extension .NET MAUI (optionnel)

## Développement

```bash
# Restaurer
dotnet restore DmTools.slnx

# Lancer sur une plateforme donnée
dotnet build DmToolsApp/DmToolsApp.csproj -f net10.0-windows10.0.19041.0
dotnet build DmToolsApp/DmToolsApp.csproj -f net10.0-android -t:Run
```

Sous Visual Studio, ouvrir `DmTools.slnx`, choisir `DmToolsApp` comme projet de
démarrage et sélectionner la cible souhaitée.

## Tests

```bash
dotnet test DmToolsApp.Tests/DmToolsApp.Tests.csproj -c Release
```

Les tests ne dépendent que de `DmToolsApp.Core`, donc ils tournent sur Linux sans les
workloads MAUI (c'est ce que fait la CI).

## Publication

Les scripts de `Deploy/` s'exécutent d'un clic droit → *Exécuter avec PowerShell* :

- **`Build-Test.ps1`** — publie Windows + Android et génère l'installeur, sans rien
  diffuser. Pour tester une build localement avant une vraie release.
- **`Build-Release.ps1`** — même build, puis crée une *GitHub Release* `v<version>`
  avec l'installeur et l'APK, et redéclenche le déploiement Netlify du site.

La signature Android utilise `Deploy/dmtools-release.keystore` (jamais commitée) et les
identifiants définis dans `Deploy/Build-Release.local.ps1` (à créer une fois par machine
depuis `Build-Release.local.ps1.example`). Sans elle, la build retombe sur le
`debug.keystore` local.

## Versionnage

Le numéro de version est `AppVersionMajor.AppVersionMinor.<nombre de commits git>` :
`major` / `minor` se règlent à la main dans `DmToolsApp/DmToolsApp.csproj`, le patch est
calculé au build (cible MSBuild `SetVersionFromGit`) et par les scripts de `Deploy/`, si
bien que l'installeur, l'APK et l'écran *Réglages* de l'app affichent toujours le même
numéro. L'app lit sa version au runtime via `AppInfo.Current.VersionString`.

## Téléchargement

Dernières versions sur la page des *Releases* du dépôt, ou depuis le site :

- Windows : installeur `.exe` (Windows 10 1809+)
- Android : APK (Android 5.0+), installation hors Play Store

## Licence

Tous droits réservés — voir [`LICENSE`](LICENSE). Le code source est publié à des fins
de consultation et de référence uniquement ; aucune permission de copie, de modification
ou de redistribution n'est accordée. L'application compilée reste gratuite via les
canaux de distribution officiels.

## Soutenir le projet

DM Tools est développé sur le temps libre, pour la communauté.
[☕ Buy Me a Coffee](https://buymeacoffee.com/narfedome) · Support : dmtools.support@gmail.com
