# DM Tools

*[Français](README.md) · English*

> Prep the sound of your tabletop RPG campaign, scene after scene.

[![Unit Tests](https://github.com/Narfedome/DmTools/actions/workflows/tests.yml/badge.svg)](https://github.com/Narfedome/DmTools/actions/workflows/tests.yml)

DM Tools is a community-made app for game masters: an audio mixer, a campaign manager
and a track library, built for a session around the table. **100% offline** — no
connection required, everything is stored locally on the device.

Website: <https://dmtools-app.netlify.app/en/> · Available for **Windows** and **Android**.

## Features

- **Audio mixer** — layer multiple tracks live (independent play, loop, fade in/out and
  volume per track) to set the mood instantly. Works on a scene or in freeform mode,
  without going through a scene.
- **Audio library** — centralize your `.mp3` tracks, reusable from one campaign to the
  next, grouped automatically by category.
- **Campaign organization** — campaigns → chapters → scenes, with drag-and-drop
  reordering, to find the right scene mid-session.
- **Import / export** — signed `.dmpack` packs (tamper detection) to share a whole
  campaign or library.
- **Visual themes** and a **French / English** interface.
- **Offline** — the SQLite database and audio files never leave the device.

## Screenshots

| Audio mixer | Track settings | Campaign organization |
|:---:|:---:|:---:|
| <img src="Website/assets/screenshots/1.png" width="240" alt="Audio mixer" /> | <img src="Website/assets/screenshots/2.png" width="240" alt="Track settings" /> | <img src="Website/assets/screenshots/3.png" width="240" alt="Campaign organization" /> |
| **Audio library** | **Import / export a campaign** | **Settings** |
| <img src="Website/assets/screenshots/4.png" width="240" alt="Audio library" /> | <img src="Website/assets/screenshots/5.png" width="240" alt="Import and export a campaign" /> | <img src="Website/assets/screenshots/6.png" width="240" alt="App settings" /> |

## Tech stack

- [.NET 10](https://dotnet.microsoft.com/) / **.NET MAUI** (`net10.0-android`,
  `net10.0-ios`, `net10.0-maccatalyst`, `net10.0-windows`)
- MVVM with [CommunityToolkit.Mvvm](https://github.com/CommunityToolkit/dotnet) and
  [CommunityToolkit.Maui](https://github.com/CommunityToolkit/Maui)
- [Plugin.Maui.Audio](https://github.com/jfversluis/Plugin.Maui.Audio) for audio playback
- [sqlite-net-pcl](https://github.com/praeclarum/sqlite-net) for local persistence
- [TagLibSharp](https://github.com/mono/taglib-sharp) for track metadata and cover art
- Tests: [xUnit](https://xunit.net/)

## Repository layout

| Project / folder        | Role |
|-------------------------|------|
| `DmToolsApp/`           | MAUI app: pages, views, components, platform services, resources. |
| `DmToolsApp.Core/`      | Shared, testable pure logic (models, SQLite data access, import/export and library services). No MAUI UI dependency. |
| `DmToolsApp.Tests/`     | xUnit unit tests, referencing only `DmToolsApp.Core` (plain net10.0, no MAUI workload). |
| `Deploy/`               | Publishing scripts (`Build-Release.ps1`, `Build-Test.ps1`), Inno Setup script (`Installer.iss`), Android keystore (never committed). |
| `Website/`              | Static marketing site (FR/EN), deployed on Netlify. |
| `.github/workflows/`    | CI: unit tests on every push/PR, unsigned iOS build on demand. |

> UI-bound `ViewModels` stay in the MAUI app; `Core` only holds logic that is genuinely
> UI-independent.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- MAUI workloads: `dotnet workload install maui`
- To build the Windows installer: [Inno Setup 6](https://jrsoftware.org/isinfo.php)
- Visual Studio 2022+ or VS Code with the .NET MAUI extension (optional)

## Development

```bash
# Restore
dotnet restore DmTools.slnx

# Run on a given platform
dotnet build DmToolsApp/DmToolsApp.csproj -f net10.0-windows10.0.19041.0
dotnet build DmToolsApp/DmToolsApp.csproj -f net10.0-android -t:Run
```

In Visual Studio, open `DmTools.slnx`, set `DmToolsApp` as the startup project and pick
the target you want.

## Tests

```bash
dotnet test DmToolsApp.Tests/DmToolsApp.Tests.csproj -c Release
```

Tests depend only on `DmToolsApp.Core`, so they run on Linux without the MAUI workloads
(that's what CI does).

## Publishing

The scripts in `Deploy/` run from a right-click → *Run with PowerShell*:

- **`Build-Test.ps1`** — publishes Windows + Android and builds the installer, without
  releasing anything. For testing a build locally before a real release.
- **`Build-Release.ps1`** — same build, then creates a *GitHub Release* `v<version>`
  with the installer and the APK, and re-triggers the Netlify site deploy.

Android signing uses `Deploy/dmtools-release.keystore` (never committed) and the
credentials set in `Deploy/Build-Release.local.ps1` (created once per machine from
`Build-Release.local.ps1.example`). Without it, the build falls back to the local
`debug.keystore`.

## Versioning

The version number is `AppVersionMajor.AppVersionMinor.<git commit count>`: `major` /
`minor` are set by hand in `DmToolsApp/DmToolsApp.csproj`, the patch is computed at build
time (the `SetVersionFromGit` MSBuild target) and by the `Deploy/` scripts, so the
installer, the APK and the app's *Settings* screen always show the same number. The app
reads its version at runtime via `AppInfo.Current.VersionString`.

## Download

Latest builds on the repository's *Releases* page, or from the website:

- Windows: `.exe` installer (Windows 10 1809+)
- Android: APK (Android 5.0+), installed outside the Play Store

## License

All rights reserved — see [`LICENSE`](LICENSE). The source code is published for viewing
and reference only; no permission is granted to copy, modify or redistribute it. The
compiled application stays free through official distribution channels.

## Support the project

DM Tools is built in spare time, for the community.
[☕ Buy Me a Coffee](https://buymeacoffee.com/narfedome) · Support: dmtools.support@gmail.com
