using System.ComponentModel;

namespace Resonance.Features.Onboarding;

public partial class OnboardingPage : ContentPage
{
    public OnboardingPage(OnboardingViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
        vm.PropertyChanged += OnViewModelPropertyChanged;
    }

    // Fondu d'entrée de la nouvelle diapo : la bascule IsVisible seule paraît sinon brutale.
    private async void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(OnboardingViewModel.CurrentStep)) return;

        StepsHost.Opacity = 0;
        await StepsHost.FadeToAsync(1, 200, Easing.CubicOut);
    }
}
