using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Features.AudioMixer;
using Resonance.Features.ImportExport;
using Resonance.Features.Library;
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

        // Windows exige 4 segments (Major.Minor.Build.Revision) pour l'identité de package - le 4e
        // (Revision) est une valeur fixe du csproj sans intérêt pour l'utilisateur, on l'aligne sur
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

        public async Task InitializeAsync()
        {
            var totalBytes = await _storageService.GetTotalLibrarySizeAsync();
            StorageUsedText = FormatSize(totalBytes);
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

        private static string FormatSize(long bytes)
        {
            string[] units = ["o", "Ko", "Mo", "Go"];
            double size = bytes;
            int unitIndex = 0;

            while (size >= 1024 && unitIndex < units.Length - 1)
            {
                size /= 1024;
                unitIndex++;
            }

            return $"{size:0.#} {units[unitIndex]}";
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
        public async Task ReportBug() =>
            await Launcher.OpenAsync(new Uri("https://docs.google.com/forms/d/e/1FAIpQLSdg2q1o01eGZsFvd0qIwOqYEVKDwBikQ0g7FWLSWHenKqeW0g/viewform?usp=dialog"));

        [RelayCommand]
        public async Task OpenImportExport() =>
            await Shell.Current.GoToAsync(nameof(ImportExportPage));

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

        public SettingsViewModel(IStorageService storageService, AudioPlayerService audioPlayerService, AudioMixerViewModel audioMixerViewModel)
        {
            _storageService = storageService;
            _audioPlayerService = audioPlayerService;
            _audioMixerViewModel = audioMixerViewModel;
            selectedLanguage = Loc.Language;
            selectedPalette  = _theme.Palette;
            RebuildThemeOptions();
            selectedThemeOption = ThemeOptions[(int)_theme.ThemePreference];

            WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this,
                (r, m) => ((SettingsViewModel)r).RebuildThemeOptions());
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
