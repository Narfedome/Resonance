using CommunityToolkit.Maui.Views;

namespace Resonance.Components.Dialogs;

/// <summary>
/// Formulaire de création/édition d'une piste, dans le même style carte que CreateItemDialog.
/// Pur wrapper XAML lié à TrackEditDialogViewModel (comme toute page de l'appli) : toute la
/// logique (choix de fichier, catégorie, persistance) vit dans le ViewModel, pas ici.
/// </summary>
public partial class TrackEditDialog : Popup<bool>
{
    public TrackEditDialog(TrackEditDialogViewModel viewModel)
    {
        InitializeComponent();
        ContentScroll.MaximumHeightRequest = DialogSizing.MaxContentHeight();
        BindingContext = viewModel;

        viewModel.CloseRequested += async saved => await CloseAsync(saved);

        // Une piste jouée en prévisualisation ne doit pas continuer après la fermeture du
        // dialogue, qu'il soit annulé ou sauvegardé.
        Closed += (_, _) => viewModel.StopAudio();
    }
}
