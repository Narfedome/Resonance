using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Features.AudioMixer;
using Resonance.Features.ImportExport;
using Resonance.Features.Library;
using Resonance.Features.Onboarding;
using Resonance.Services;
using System.Collections.ObjectModel;

namespace Resonance.Features.Settings
{
    public partial class SettingsViewModel : BaseViewModel
    {
        private readonly ThemeService _theme = ThemeService.Instance;
        private readonly IStorageService _storageService;
        private readonly AudioPlayerService _audioPlayerService;
        private readonly AudioMixerViewModel _audioMixerViewModel;
        private readonly SamplePackService _samplePackService;
        private readonly AppTourService _tourService;

        // Windows exige 4 segments (Major.Minor.Build.Revision) pour l'identité de package - le 4e
        // (Revision) vaut toujours 0 (exigence du Microsoft Store), sans intérêt pour l'utilisateur, on l'aligne sur
        // le format 3 segments affiché nativement sur les autres plateformes.
        public string AppVersion
        {
            get
            {
                var raw = AppInfo.Current.VersionString;
                var parts = raw.Split('.');
                return parts.Length > 3 ? string.Join('.', parts.Take(3)) : raw;
            }
        }

        [ObservableProperty]
        private string storageUsedText = string.Empty;

        private long _storageUsedBytes;

        public async Task InitializeAsync()
        {
            _storageUsedBytes = await _storageService.GetTotalLibrarySizeAsync();
            StorageUsedText = FormatSize(_storageUsedBytes);
            HasCrashLog = CrashLogger.HasEntries;
        }

        [RelayCommand]
        public async Task CleanupStorage()
        {
            var scan = await _storageService.ScanOrphansAsync();

            if (scan.OrphanedFiles.Count == 0)
            {
                await ShowInfoAsync(Loc["SettingsStorageTitle"], Loc["SettingsStorageNothingToClean"]);
                return;
            }

            var message = string.Format(Loc["SettingsStorageCleanupConfirmMessage"], scan.OrphanedFiles.Count, FormatSize(scan.TotalOrphanBytes));
            if (!await ConfirmAsync(Loc["SettingsStorageCleanupConfirmTitle"], message))
                return;

            try
            {
                var freed = await _storageService.DeleteOrphansAsync(scan);
                await InitializeAsync();
                await ShowInfoAsync(Loc["SettingsStorageTitle"], string.Format(Loc["SettingsStorageCleanupResult"], FormatSize(freed)));
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        [RelayCommand]
        public async Task DeleteAllTracks()
        {
            var isPlaying = _audioPlayerService.CurrentFile != null
                || _audioMixerViewModel.CurrentChannels.Any(c => c.IsPlaying);

            if (isPlaying)
            {
                await ShowInfoAsync(Loc["SettingsStorageTitle"], Loc["SettingsStorageDeleteAllPlaybackWarning"]);
                return;
            }

            var (count, totalBytes) = await _storageService.GetTracksSummaryAsync();

            if (count == 0)
            {
                await ShowInfoAsync(Loc["SettingsStorageTitle"], Loc["SettingsStorageDeleteAllNothingToDelete"]);
                return;
            }

            var message = string.Format(Loc["SettingsStorageDeleteAllConfirmMessage"], count, FormatSize(totalBytes));
            if (!await ConfirmAsync(Loc["SettingsStorageDeleteAllConfirmTitle"], message))
                return;

            if (!await ConfirmAsync(Loc["SettingsStorageDeleteAllConfirmTitle"], Loc["SettingsStorageDeleteAllFinalConfirmMessage"]))
                return;

            try
            {
                // Vide le mixer avant de supprimer : des strips chargés (même en pause) gardent
                // leurs fichiers ouverts (verrouillés sur Windows) et pointeraient de toute façon
                // vers des pistes qui n'existent plus.
                _audioMixerViewModel.ClearChannels();

                var deleted = await _storageService.DeleteAllTracksAsync();

                // Sans ça, la page Bibliothèque (qui ne recharge sa liste qu'au premier affichage ou
                // sur ce message) continue d'afficher des pistes qui n'existent plus une fois qu'on y
                // revient depuis Réglages.
                WeakReferenceMessenger.Default.Send(new LibraryUpdatedMessage());

                await InitializeAsync();
                await ShowInfoAsync(Loc["SettingsStorageTitle"], string.Format(Loc["SettingsStorageDeleteAllResult"], deleted));
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        // Unités traduites (o/Ko/Mo/Go en français, B/KB/MB/GB en anglais) : codées en dur, l'interface
        // anglaise affichait "1.6 Mo".
        private string FormatSize(long bytes)
        {
            string[] units = Loc["SizeUnits"].Split('|');
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            // Séparateur décimal de la langue de l'appli, pas du système (cf. LocalizationService.Culture).
            return $"{size.ToString("0.#", Loc.Culture)} {units[unitIndex]}";
        }

        [RelayCommand]
        public async Task SelectLanguage()
        {
            var labels = LanguageLabels.Values.ToArray();
            var result = await ShowActionSheetAsync(Loc["SettingsLanguage"], labels);
            if (result == null) return;
            SelectedLanguage = LanguageLabels.First(kv => kv.Value == result).Key;
        }

        [RelayCommand]
        public async Task SelectTheme()
        {
            var result = await ShowActionSheetAsync(Loc["SettingsTheme"], ThemeOptions.ToArray());
            if (result != null) SelectedThemeOption = result;
        }

        [RelayCommand]
        public async Task OpenCoffeeLink() =>
            await Launcher.OpenAsync(new Uri("https://buymeacoffee.com/narfedome"));

        [RelayCommand]
        public async Task ReportBug()
        {
            // « Nous écrire » dans l'appli dès que le relais commun est déployé (FeedbackService.Endpoint), l'ancien
            // formulaire Google en attendant.
            if (!FeedbackService.IsConfigured)
            {
                await Launcher.OpenAsync(FeedbackService.LegacyFormUri);
                return;
            }
            var viewModel = new Feedback.FeedbackDialogViewModel();
            if (await ShowDialogAsync(new Feedback.FeedbackDialog(viewModel)) is not true) return;
            var (title, message) = viewModel.Result switch
            {
                Core.Services.FeedbackSendResult.Retry => (Loc["FeedbackQueuedTitle"], Loc["FeedbackQueued"]),
                Core.Services.FeedbackSendResult.RateLimited => (Loc["FeedbackRateLimitedTitle"], Loc["FeedbackRateLimited"]),
                _ => (Loc["FeedbackSentTitle"], Loc["FeedbackSent"])
            };
            await ShowInfoAsync(title, message);
        }

        /// <summary>Relu à chaque affichage de la page (cf. InitializeAsync) : le journal grossit pendant l'utilisation.</summary>
        [ObservableProperty]
        private bool hasCrashLog;

        // Partage système (e-mail, messagerie, enregistrement...) : le formulaire de signalement ne
        // prend pas de pièce jointe, l'utilisateur envoie ainsi le journal par le canal de son choix.
        [RelayCommand]
        public async Task ShareCrashLog()
        {
            try
            {
                await Share.Default.RequestAsync(new ShareFileRequest
                {
                    Title = Loc["SettingsCrashLogShareTitle"],
                    File = new ShareFile(CrashLogger.CreateShareableCopy(), "text/plain")
                });
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        // Sous Windows, le partage système ne s'ouvre pas pour l'appli non empaquetée (rien ne se passe,
        // sans erreur) : on ouvre plutôt l'Explorateur sur la copie du journal, déjà sélectionnée, prête
        // à être glissée dans un e-mail.
        [RelayCommand]
        public async Task ShowCrashLog()
        {
            try
            {
#if WINDOWS
                System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{CrashLogger.CreateShareableCopy()}\"");
#else
                await ShareCrashLog();
#endif
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        [RelayCommand]
        public async Task OpenImportExport() =>
            await Shell.Current.GoToAsync(nameof(ImportExportPage));

        [RelayCommand]
        public async Task LoadSampleCampaign()
        {
            // L'import crée toujours de nouvelles campagnes : un second chargement en ferait une copie.
            if (_samplePackService.HasImportedSample &&
                !await ConfirmAsync(Loc["OnboardingLoadSample"], Loc["SettingsSampleAlreadyImported"]))
                return;

            try
            {
                await _samplePackService.ImportAsync(Shell.Current.CurrentPage);
                await InitializeAsync();
                await ShowInfoAsync(Loc["OnboardingLoadSample"], Loc["SampleImportDone"]);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        // Fait revenir les visites de chaque écran ; celle des Campagnes démarre tout de suite (cf.
        // CampaignPage), les autres à la prochaine arrivée sur leur écran.
        [RelayCommand]
        public async Task StartTour()
        {
            _tourService.ResetAll();
            await Shell.Current.GoToAsync("//CampaignPage");
        }

        // Nouvelle instance de l'accueil (Transient) : repart de la première étape. Son dernier bouton
        // ramène sur l'AppShell (singleton), resté tel quel entre-temps.
        [RelayCommand]
        public void ReplayWelcome() =>
            Application.Current!.Windows[0].Page = IPlatformApplication.Current!.Services.GetRequiredService<OnboardingPage>();

        public static Dictionary<string, string> LanguageLabels => LocalizationService.SupportedLanguages;

        public ObservableCollection<string> ThemeOptions { get; } = new();

        [ObservableProperty]
        private string selectedLanguage;

        public string? SelectedLanguageLabel =>
            LanguageLabels.TryGetValue(SelectedLanguage ?? "", out var label) ? label : SelectedLanguage;

        [ObservableProperty]
        private AppPalette selectedPalette;

        [ObservableProperty]
        private string selectedThemeOption;

        public SettingsViewModel(IStorageService storageService, AudioPlayerService audioPlayerService, AudioMixerViewModel audioMixerViewModel,
            SamplePackService samplePackService, AppTourService tourService)
        {
            _storageService = storageService;
            _audioPlayerService = audioPlayerService;
            _audioMixerViewModel = audioMixerViewModel;
            _samplePackService = samplePackService;
            _tourService = tourService;
            selectedLanguage = Loc.Language;
            selectedPalette  = _theme.Palette;
            RebuildThemeOptions();
            selectedThemeOption = ThemeOptions[(int)_theme.ThemePreference];

            WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this,
                (r, m) =>
                {
                    var vm = (SettingsViewModel)r;
                    vm.RebuildThemeOptions();
                    vm.StorageUsedText = vm.FormatSize(vm._storageUsedBytes);
                });
        }

        private void RebuildThemeOptions()
        {
            var current = _theme.ThemePreference;

            string[] labels = [Loc["SettingsThemeSystem"], Loc["SettingsThemeLight"], Loc["SettingsThemeDark"]];
            for (int i = 0; i < labels.Length; i++)
            {
                if (i < ThemeOptions.Count) ThemeOptions[i] = labels[i];
                else ThemeOptions.Add(labels[i]);
            }

            _rebuildingOptions = true;
            SelectedThemeOption = ThemeOptions[(int)current];
            _rebuildingOptions = false;
        }

        private bool _rebuildingOptions;

        partial void OnSelectedLanguageChanged(string value)
        {
            if (value != null) Loc.Language = value;
            OnPropertyChanged(nameof(SelectedLanguageLabel));
        }

        partial void OnSelectedPaletteChanged(AppPalette value)
        {
            _theme.Palette = value;
        }

        partial void OnSelectedThemeOptionChanged(string value)
        {
            if (value is null || _rebuildingOptions) return;
            int idx = ThemeOptions.IndexOf(value);
            if (idx >= 0) _theme.ThemePreference = (AppThemePreference)idx;
        }
    }
}
