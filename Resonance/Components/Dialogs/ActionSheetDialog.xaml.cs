using CommunityToolkit.Maui.Views;

namespace Resonance.Components.Dialogs;

/// <summary>Pur wrapper XAML lié à ActionSheetDialogViewModel : toute la logique y vit, pas ici.</summary>
public partial class ActionSheetDialog : Popup<int>
{
    public ActionSheetDialog(ActionSheetDialogViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
        viewModel.CloseRequested += async index => await CloseAsync(index);
    }
}
