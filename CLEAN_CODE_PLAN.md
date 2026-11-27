# Plan de chantier Clean Code / SOLID — Resonance

Préparé le 2026-11-27 sur la branche par défaut de `Narfedome/Resonance`. Une branche `feature/<nom>` par lot, du plus
rentable au plus lourd. Les numéros de ligne datent de ce relevé.

Validation : build de la tête MAUI (cible Windows) + `dotnet test Resonance.Tests`.

## Correctifs du premier bilan (rien à faire)

- **`async void`** : les 11 de l'appli sont des gestionnaires de cycle de vie/d'événement, et leur travail passe par
  `UiErrors.RunAsync` ou un `try` explicite (`AudioMixerPage.xaml.cs:116` et `CampaignPage.xaml.cs:29` compris). Conforme.
- **`catch` génériques** : à des frontières (journal, fichiers, import) et commentés. Conforme.

---

## Lot 1 — Sortir `Plugin.Maui.Audio` de Core (DIP / Clean Architecture) · ~1 h · priorité 1

`Resonance.Core/Features/AudioMixer/ChannelStripViewModel.cs` dépend d'`IAudioPlayer` (l. 4, 30-31), ce qui oblige
`Resonance.Core.csproj` à référencer `Plugin.Maui.Audio`. C'est le seul fichier de Core concerné. Le ViewModel a été
mis dans Core pour être testé (`ChannelStripViewModelTests`, 835 l.) : c'est une bonne raison, il faut garder ça.

1. `Resonance.Core/Audio/IChannelPlayer.cs` (nouveau) : uniquement les membres réellement utilisés par le ViewModel,
   `Volume`, `Loop`, `IsPlaying`, `Duration`, `CurrentPosition`, `Play()`, `Pause()`, `Stop()`, `Seek(double)`,
   `event EventHandler PlaybackEnded`, `IDisposable`.
2. `ChannelStripViewModel.cs` : `IAudioPlayer` → `IChannelPlayer`, retirer `using Plugin.Maui.Audio;`.
3. Tête MAUI, `Resonance/Services/AudioMixerService.cs` : `CreatePlayerAsync` (l. 15) renvoie `Task<IChannelPlayer>`.
   Un adaptateur `PluginChannelPlayer : IChannelPlayer` enveloppe l'`IAudioPlayer` du plugin (et absorbe
   `StreamOwningAudioPlayer`, l. 34, qui possède déjà le flux).
4. Appelants inchangés en dehors du type : `AudioMixerViewModel.Channels.cs:138` et `:410` (`channel.Player = await ...`).
5. `Resonance.Core.csproj` : retirer `<PackageReference Include="Plugin.Maui.Audio" .../>`. S'il compile encore,
   Core ne dépend plus de MAUI.
6. Tests : `FakeAudioPlayer` (`ChannelStripViewModelTests.cs:781`) implémente `IChannelPlayer` au lieu
   d'`IAudioPlayer` (membres déjà présents, il suffit de retirer ceux qui ne sont pas dans l'interface, comme `Error`).

---

## Lot 2 — Services concrets injectés → interfaces (DIP) · ~1 h 30

Les services de données ont déjà leur interface (`ILibraryDataService`, `ISceneDataService`, `IImportExportService`,
`IStorageService`, `ITrackFileStore`), mais les ViewModels reçoivent encore 7 services concrets de la tête MAUI :

| Service concret | Injecté dans |
|---|---|
| `AudioPlayerService` | `TrackButtonViewModel:14`, `SettingsViewModel:264`, `LibraryTrackViewModel:202` |
| `AudioMixerService` | `AudioMixerViewModel:23` |
| `FileService` | `LibraryTrackViewModel:202`, `ImportExportViewModel:31` (déjà exposé en `ITrackFileStore`) |
| `CoverArtService` | `TrackButtonViewModel:14` |
| `PackImportService`, `SamplePackService` | `ImportExportViewModel:31`, `SettingsViewModel:264`, `OnboardingViewModel:43` |
| `AppTourService` | `CampaignViewModel:26`, `SettingsViewModel:264` |

Priorité aux 3 premiers (lecture audio et fichiers, ce qu'on voudrait simuler dans un test) : `IAudioPlayerService`,
`IAudioMixerService`, et pour `FileService` une interface `IFileService` ou l'élargissement d'`ITrackFileStore`.
`MauiProgram.cs` l. 84-86 : enregistrer sous l'interface, sur le modèle de la l. 87. Les autres (tour guidé, packs)
sont optionnels.

---

## Lot 3 — `AudioMixerViewModel` partagé entre ViewModels (couplage) · ~2 h

`AudioMixerViewModel` est un singleton (`MauiProgram.cs:106`) injecté dans `CampaignViewModel:26` et
`SettingsViewModel:264`. Un ViewModel qui dépend d'un autre ViewModel lie deux écrans entre eux.

1. Ce que ces 2 ViewModels appellent réellement (relevé) :
   - `SettingsViewModel.cs:106` : `ClearChannels`
   - `CampaignViewModel.cs` : `IsActiveCampaign` (452), `IsActiveSession` (459), `IsActiveScene` (466),
     `ResetActiveScene` (482), `SuppressNextFreeformAutoLoad` (519), `LoadFromPlayAsync` (521), `CurrentChannels` (529)
2. Extraire ces 8 membres dans un service `IMixerSession` (scène/session/campagne actives, chargement d'une scène,
   vidage des pistes), singleton, que `AudioMixerViewModel` utilise aussi. Les 3 ViewModels dépendent alors du
   service, plus les uns des autres.

---

## Lot 4 — Localisateur de services · ~30 min

- `Components/TrackButton/TrackButtonView.xaml.cs:64-65` va chercher `AudioPlayerService` et `CoverArtService` dans
  `Application.Current.Handler.MauiContext.Services` pour construire son ViewModel. Contrôle réutilisable instancié
  par le XAML : pas de DI possible par constructeur. Garder, mais résoudre une `Func<TrackButtonViewModel>` enregistrée
  (une seule résolution au lieu de deux) ou documenter l'exception.
- `Features/Settings/SettingsViewModel.cs:246` (`OnboardingPage`) : injecter `Func<OnboardingPage>` (même correction
  que dans Mordheim Ledger).
- `BaseViewModel.cs:21` (`LoadingService`), `Components/Dialogs/ChannelSettingsDialog.xaml.cs:21` (`AppTourService`) :
  exceptions de frontière UI, à commenter.

---

## Lot 5 — `LibraryTrackViewModel` (SRP) · ~2 h

803 lignes : chargement paginé + recherche + catégories + sélection multiple + import + suppression.
- `ImportMultipleTracks` + `PickImportCategoryAsync` (l. 658–fin, ~150 l.) → `TrackImportCoordinator` (appli), qui
  reçoit `ILibraryDataService` + `IFileService`.
- Chargement paginé et recherche différée (`EnsureFreshLoadToken`, `ReloadAfterSearchPauseAsync`, `ReloadCoreAsync`,
  `LoadNextPageCoreAsync`, l. 52–520) → `PagedTrackLoader`, testable en Core (pas de dépendance MAUI dans cette logique).
- La sélection multiple a déjà sa classe (`Resonance.Core/Features/Library/LibraryMultiSelection.cs`) : vérifier que
  `SelectAll`/`DeleteSelectedItems` (l. 554–627) passent bien par elle.

`AudioMixerViewModel` (563 + 433 l. en `partial`) et `CampaignViewModel` (538 l.) restent dans des tailles acceptables :
à surveiller, pas de lot.

---

## Accepté tel quel

- `LocalizationService.Instance` / `ThemeService.Instance` (36 usages) : même motif que Mordheim Ledger, cohérent.
- `ChannelStripViewModel.UiDispatcher` statique (`MauiProgram.cs:72`) : injection minimale qui garde Core sans MAUI,
  déjà commentée.
