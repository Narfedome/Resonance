using CommunityToolkit.Maui.Views;

namespace Resonance.Components.Dialogs;

/// <summary>Pur wrapper XAML lié à CreateItemDialogViewModel : toute la logique y vit, pas ici.</summary>
public partial class CreateItemDialog : Popup<bool>
{
    public CreateItemDialog(CreateItemDialogViewModel viewModel)
    {
        InitializeComponent();
        ContentScroll.MaximumHeightRequest = DialogSizing.MaxContentHeight();
        BindingContext = viewModel;
        viewModel.CloseRequested += async saved => await CloseAsync(saved);

        Opened += async (_, _) =>
        {
            await viewModel.InitializeAsync();

            // Focus direct sur le champ Nom à l'ouverture, curseur en fin de texte pré-rempli (édition) :
            // mécanique UI pure (pas de logique métier), reste en code-behind comme dans les autres
            // dialogues - il n'y a pas de moyen déclaratif de bind ça côté MAUI.
            NameEntry.Focus();
            NameEntry.CursorPosition = NameEntry.Text?.Length ?? 0;
        };
    }
}
