namespace Resonance.Components;

/// <summary>
/// Glisser-déposer des canaux du mixeur (tranches d'ambiance et pads d'effet). Un seul drag à la
/// fois : la vue glissée est suivie ici, au niveau de la classe, pour que la page puisse la
/// remettre à son état normal au Drop en filet de sécurité, DropCompleted ne se déclenchant pas
/// toujours côté source sur Android (bug connu dotnet/maui#17554).
/// </summary>
public static class ChannelDrag
{
    private static VisualElement? _dragging;

    // Seul l'item glissé est transmis (pas un index), pour rester valide même si l'ordre a changé
    // entre le début du drag et le drop (cf. AudioMixerViewModel.ReorderChannelsAsync).
    public static void Start(VisualElement view, DragStartingEventArgs e)
    {
        if (view.BindingContext is not ChannelStripViewModel channel) return;

        e.Data.Properties["Channel"] = channel;
        _dragging = view;
        view.Opacity = 0.55;
    }

    public static void Reset()
    {
        if (_dragging == null) return;
        _dragging.Opacity = 1;
        _dragging = null;
    }
}
