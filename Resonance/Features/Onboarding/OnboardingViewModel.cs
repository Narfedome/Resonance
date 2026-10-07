using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Services;

namespace Resonance.Features.Onboarding
{
    public partial class OnboardingViewModel : BaseViewModel
    {
        private readonly ThemeService _theme = ThemeService.Instance;
        private readonly AppShell _shell;

        public string SelectedLanguageLabel =>
            LocalizationService.SupportedLanguages.TryGetValue(SelectedLanguage ?? "", out var label) ? label : SelectedLanguage ?? "";

        [ObservableProperty]
        private string selectedLanguage;

        [ObservableProperty]
        private AppPalette selectedPalette;

        public OnboardingViewModel(AppShell shell)
        {
            _shell = shell;
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
        private void Start()
        {
            Preferences.Default.Set("has_launched", true);
            Application.Current!.Windows[0].Page = _shell;
        }
    }
}
