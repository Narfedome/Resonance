using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Services;

namespace Resonance.Features.Onboarding
{
    public partial class OnboardingViewModel : BaseViewModel
    {
        // Étapes de l'accueil : Bienvenue (+ langue), Campagne › chapitre › scène, Bibliothèque et
        // mixeur, puis palette et choix entre la campagne d'exemple et une install vide.
        private const int StepCount = 4;

        private readonly ThemeService _theme = ThemeService.Instance;
        private readonly AppShell _shell;
        private readonly SamplePackService _samplePackService;
        private readonly AppTourService _tourService;

        public string SelectedLanguageLabel =>
            LocalizationService.SupportedLanguages.TryGetValue(SelectedLanguage ?? "", out var label) ? label : SelectedLanguage ?? "";

        [ObservableProperty]
        private string selectedLanguage;

        [ObservableProperty]
        private AppPalette selectedPalette;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsWelcomeStep), nameof(IsStructureStep), nameof(IsPlayStep), nameof(IsReadyStep), nameof(IsFirstStep))]
        private int currentStep;

        // Un booléen par étape plutôt qu'un convertisseur "étape == N" : la page lie directement
        // IsVisible de chaque diapo et l'état de chaque point de progression.
        public bool IsWelcomeStep => CurrentStep == 0;
        public bool IsStructureStep => CurrentStep == 1;
        public bool IsPlayStep => CurrentStep == 2;
        public bool IsReadyStep => CurrentStep == StepCount - 1;
        public bool IsFirstStep => CurrentStep == 0;

        [ObservableProperty]
        private bool isImportingSample;

        public OnboardingViewModel(AppShell shell, SamplePackService samplePackService, AppTourService tourService)
        {
            _shell = shell;
            _samplePackService = samplePackService;
            _tourService = tourService;
            selectedLanguage = Loc.Language;
            selectedPalette  = _theme.Palette;
        }

        partial void OnSelectedLanguageChanged(string value)
        {
            if (value != null) Loc.Language = value;
            OnPropertyChanged(nameof(SelectedLanguageLabel));
        }

        partial void OnSelectedPaletteChanged(AppPalette value)
        {
            _theme.Palette = value;
        }

        [RelayCommand]
        public async Task SelectLanguage()
        {
            var labels = LocalizationService.SupportedLanguages.Values.ToArray();
            var result = await ShowActionSheetAsync(Loc["OnboardingLanguage"], labels);
            if (result == null) return;
            SelectedLanguage = LocalizationService.SupportedLanguages.First(kv => kv.Value == result).Key;
        }

        [RelayCommand]
        private void Next()
        {
            if (CurrentStep < StepCount - 1) CurrentStep++;
        }

        [RelayCommand]
        private void Previous()
        {
            if (CurrentStep > 0) CurrentStep--;
        }

        [RelayCommand]
        private async Task LoadSample()
        {
            if (IsImportingSample) return;

            // L'accueil peut être revu depuis les Réglages : même garde-fou que là-bas contre une
            // seconde copie de la campagne (l'import crée toujours de nouvelles campagnes).
            if (_samplePackService.HasImportedSample &&
                !await ConfirmAsync(Loc["OnboardingLoadSample"], Loc["SettingsSampleAlreadyImported"]))
                return;

            IsImportingSample = true;
            try
            {
                await _samplePackService.ImportAsync(Application.Current!.Windows[0].Page!);

                // La visite s'appuie sur la campagne d'exemple : seulement si elle a bien été chargée.
                _tourService.Request();
            }
            catch (Exception ex)
            {
                // L'accueil se termine malgré tout : l'utilisateur arrive sur l'état vide habituel
                // des Campagnes, et peut retenter depuis les Réglages.
                await ShowErrorAsync(ex);
            }
            finally
            {
                IsImportingSample = false;
            }

            await FinishAsync();
        }

        [RelayCommand]
        private Task StartEmpty() => FinishAsync();

        private async Task FinishAsync()
        {
            Preferences.Default.Set("has_launched", true);
            Application.Current!.Windows[0].Page = _shell;

            // Accueil revu depuis les Réglages : l'AppShell reprend sur l'onglet Réglages, où la visite
            // (lancée par CampaignPage) ne se déclencherait jamais. Au premier lancement, les Campagnes
            // sont déjà l'onglet affiché et cette navigation ne change rien.
            try
            {
                await _shell.GoToAsync("//CampaignPage");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Onboarding] {ex}");
            }
        }
    }
}
