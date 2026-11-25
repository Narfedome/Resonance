namespace Resonance.Components;

public partial class MixerConsoleStripView : ContentView
{
    // Tranches actuellement affichées. Quand la liste grandit (panneau d'actions refermé, fenêtre
    // agrandie), Android ne re-mesure pas de lui-même les éléments déjà affichés d'une
    // CollectionView : certaines tranches restaient courtes à côté de voisines à la bonne hauteur.
    // La page les fait re-mesurer toutes via RemeasureAll.
    private static readonly List<WeakReference<MixerConsoleStripView>> Realized = new();

    public MixerConsoleStripView()
    {
        InitializeComponent();
        Loaded += (_, _) => Realized.Add(new WeakReference<MixerConsoleStripView>(this));
        Unloaded += (_, _) => Realized.RemoveAll(r => !r.TryGetTarget(out var s) || ReferenceEquals(s, this));
    }

    public static void RemeasureAll()
    {
        Realized.RemoveAll(r => !r.TryGetTarget(out _));
        foreach (var reference in Realized)
        {
            if (reference.TryGetTarget(out var strip))
                strip.InvalidateMeasure();
        }
    }

    private void OnDragStarting(object? sender, DragStartingEventArgs e) => ChannelDrag.Start(this, e);

    private void OnDropCompleted(object? sender, DropCompletedEventArgs e) => ChannelDrag.Reset();
}
