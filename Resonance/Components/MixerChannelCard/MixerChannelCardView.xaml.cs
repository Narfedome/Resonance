namespace Resonance.Components;

public partial class MixerChannelCardView : ContentView
{
    // Un seul drag possible à la fois dans la liste : suivre la carte en cours de glissement au
    // niveau de la classe (plutôt que via une instance passée à la page) permet à
    // AudioMixerPage.OnChannelDrop de réinitialiser le fondu en filet de sécurité même si
    // DropCompleted ne se déclenche pas de façon fiable sur Android (bug connu
    // dotnet/maui#17554), sans que la page n'ait besoin de connaître la carte en cours.
    private static MixerChannelCardView? _draggingCard;

    public MixerChannelCardView()
    {
        InitializeComponent();
    }

    // Cf. SceneDataService.ReorderSceneTracksAsync : AudioMixerPage.OnChannelDrop ne stocke que
    // l'item glissé (pas un index), pour rester valide même si l'ordre a changé entre le début du
    // drag et le drop.
    private void OnDragStarting(object sender, DragStartingEventArgs e)
    {
        if (BindingContext is not ChannelStripViewModel channel) return;

        e.Data.Properties["Channel"] = channel;

        _draggingCard = this;
        Opacity = 0.55;
        TranslationX = 10;
    }

    private void OnDropCompleted(object sender, DropCompletedEventArgs e) => ResetDraggingCard();

    /// <summary>
    /// Remet la carte en cours de glissement (s'il y en a une) à son état normal. Appelée en
    /// interne par OnDropCompleted, et par AudioMixerPage.OnChannelDrop en filet de sécurité (cf.
    /// commentaire sur _draggingCard).
    /// </summary>
    public static void ResetDraggingCard()
    {
        if (_draggingCard == null) return;
        _draggingCard.Opacity = 1;
        _draggingCard.TranslationX = 0;
        _draggingCard = null;
    }
}
