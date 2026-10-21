using CommunityToolkit.Maui.Views;
using Resonance.Services;

namespace Resonance.Components.Dialogs;

/// <summary>
/// Wrapper XAML lié à ChannelSettingsDialogViewModel, où vit toute la logique du dialogue. Seul ajout ici :
/// la visite guidée de ses options, à la première ouverture (cf. AppTourService) - un écran à part
/// entière, plutôt qu'une suite de la visite du mixeur qui en devenait trop longue.
/// </summary>
public partial class ChannelSettingsDialog : Popup<bool>
{
    public ChannelSettingsDialog(ChannelSettingsDialogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.CloseRequested += async result => await CloseAsync(result);

        Opened += async (_, _) =>
        {
            var tourService = IPlatformApplication.Current?.Services.GetService<AppTourService>();
            if (tourService is not { IsChannelSettingsTourPending: true })
                return;

            try
            {
                await tourService.StartChannelSettingsTourAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Tour] {ex}");
            }
        };
    }
}
