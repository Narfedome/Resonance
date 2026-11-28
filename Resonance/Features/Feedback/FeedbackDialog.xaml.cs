using CommunityToolkit.Maui.Views;
using Resonance.Components.Dialogs;

namespace Resonance.Features.Feedback;

/// <summary>Formulaire « Nous écrire », ouvert depuis Paramètres - même carte que TrackEditDialog.</summary>
public partial class FeedbackDialog : Popup<bool>
{
    public FeedbackDialog(FeedbackDialogViewModel viewModel)
    {
        InitializeComponent();
        ContentScroll.MaximumHeightRequest = DialogSizing.MaxContentHeight();
        BindingContext = viewModel;
        viewModel.CloseRequested += async sent => await CloseAsync(sent);
    }
}
