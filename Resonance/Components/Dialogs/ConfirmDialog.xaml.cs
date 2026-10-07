using CommunityToolkit.Maui.Views;

namespace Resonance.Components.Dialogs;

/// <summary>Pur wrapper XAML lié à ConfirmDialogViewModel : toute la logique y vit, pas ici.</summary>
public partial class ConfirmDialog : Popup<bool>
{
    public ConfirmDialog(ConfirmDialogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.CloseRequested += async result => await CloseAsync(result);
    }
}
