using System.ComponentModel;
using Resonance.Services;

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
        // Effet purement visuel : un échec est journalisé sans message, la diapo reste juste visible.
        await UiErrors.RunAsync(async () =>
        {
            try
            {
                await StepsHost.FadeToAsync(1, 200, Easing.CubicOut);
            }
            finally
            {
                StepsHost.Opacity = 1;
            }
        }, "OnboardingPage fade", showMessage: false);
    }
}
