using CommunityToolkit.Maui.Views;

namespace Resonance.Components.Dialogs;

/// <summary>Pur wrapper XAML lié à PromptDialogViewModel : toute la logique y vit, pas ici.</summary>
public partial class PromptDialog : Popup<string?>
{
    public PromptDialog(PromptDialogViewModel viewModel)
    {
        InitializeComponent();
        ContentScroll.MaximumHeightRequest = DialogSizing.MaxContentHeight();
        BindingContext = viewModel;
        viewModel.CloseRequested += async result => await CloseAsync(result);

        // Focus direct sur le champ de saisie à l'ouverture, pour pouvoir taper sans clic préalable.
        // Sans CursorPosition, le curseur reste par défaut au début du texte pré-rempli (rename) —
        // gênant pour ajouter la fin d'un nom existant. Mécanique UI pure (pas de logique métier) :
        // reste en code-behind, il n'y a pas de moyen déclaratif de bind ça côté MAUI.
        Opened += (_, _) =>
        {
            InputEntry.Focus();
            InputEntry.CursorPosition = InputEntry.Text?.Length ?? 0;
        };
    }
}
