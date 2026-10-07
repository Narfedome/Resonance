using CommunityToolkit.Maui.Views;

namespace Resonance.Components.Dialogs;

/// <summary>Pur wrapper XAML lié à ChannelSettingsDialogViewModel : toute la logique y vit, pas ici.</summary>
public partial class ChannelSettingsDialog : Popup<bool>
{
    public ChannelSettingsDialog(ChannelSettingsDialogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.CloseRequested += async result => await CloseAsync(result);
    }
}
