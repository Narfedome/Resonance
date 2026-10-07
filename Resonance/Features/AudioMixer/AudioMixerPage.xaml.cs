using Resonance.Components;

namespace Resonance.Features.AudioMixer;

public partial class AudioMixerPage : ContentPage
{
    private readonly AudioMixerViewModel _vm;

    private static Color AccentColor => (Color)Application.Current!.Resources["AppAccent"];
    private static Color HighlightBackgroundColor => AccentColor.WithAlpha(0.12f);

    // Auto-scroll pendant le drag : CollectionView ne le fait pas nativement quand on approche
    // un bord alors que la liste horizontale déborde (scrollbar visible). ScrollTo n'accepte
    // qu'un index (pas un offset en pixels) : on avance/recule d'un item par tick tant que le
    // pointeur reste près du bord.
    private const double AutoScrollEdgeThreshold = 60;
    private IDispatcherTimer? _autoScrollTimer;
    private int _autoScrollDirection;
    private int _autoScrollIndex;

    public AudioMixerPage(AudioMixerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    // Ouvrir le Mixer sans rien avoir chargé (onglet toujours visible, cf. AppShell) affichait un
    // état vide avec le "+" désactivé (HasActiveScene) sans indiquer comment en sortir. Charger la
    // scène orpheline automatiquement dans ce cas précis - jamais si une scène est déjà active,
    // ni si CampaignViewModel.Launch est en train de charger une vraie scène (cf.
    // SuppressNextFreeformAutoLoad, positionné avant la navigation - pas de race possible ici).
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (_vm.SuppressNextFreeformAutoLoad)
        {
            _vm.SuppressNextFreeformAutoLoad = false;
            return;
        }

        if (!_vm.HasActiveScene)
            await _vm.SelectFreeformScene();
    }

    // DragHighlightBorder est un Border transparent superposé à chaque carte (cf. XAML) :
    // indépendant du Border du strip lui-même (dont le Stroke est déjà piloté par IsPlaying),
    // pour ne jamais entrer en conflit avec cet indicateur. FindByName cherche dans le namescope
    // de CETTE instance de DataTemplate réalisée par la CollectionView.
    private static void SetHighlightBorder(VisualElement root, Color stroke, Color background)
    {
        if (root.FindByName<Border>("DragHighlightBorder") is not Border border) return;
        border.Stroke = stroke;
        border.BackgroundColor = background;
    }

    private void OnChannelDragOver(object sender, DragEventArgs e)
    {
        if (sender is not VisualElement { BindingContext: ChannelStripViewModel hovered } element)
            return;

        SetHighlightBorder(element, AccentColor, HighlightBackgroundColor);
        UpdateAutoScroll(e, hovered);
    }

    private void OnChannelDragLeave(object sender, DragEventArgs e)
    {
        if (sender is VisualElement element)
            SetHighlightBorder(element, Colors.Transparent, Colors.Transparent);
        // L'auto-scroll n'est PAS arrêté ici : ce handler se déclenche aussi en passant d'une
        // carte à sa voisine (juste avant le DragOver de la suivante), l'arrêter à chaque fois
        // ferait repartir la progression de zéro à chaque item traversé.
    }

    private async void OnChannelDrop(object sender, DropEventArgs e)
    {
        if (sender is VisualElement targetElement)
            SetHighlightBorder(targetElement, Colors.Transparent, Colors.Transparent);
        // Filet de sécurité : sur Android, MixerChannelCardView.OnDropCompleted (côté source du
        // drag) ne se déclenche pas toujours de façon fiable (bug connu dotnet/maui#17554).
        MixerChannelCardView.ResetDraggingCard();
        StopAutoScroll();

        if (sender is not Element { BindingContext: ChannelStripViewModel target })
            return;
        if (!e.Data.Properties.TryGetValue("Channel", out var draggedObj) || draggedObj is not ChannelStripViewModel dragged)
            return;

        await _vm.ReorderChannelsAsync(dragged, target);
    }

    private void UpdateAutoScroll(DragEventArgs e, ChannelStripViewModel hovered)
    {
        var position = e.GetPosition(ChannelsCollectionView);
        if (position is null || ChannelsCollectionView.Width <= 0)
        {
            StopAutoScroll();
            return;
        }

        double x = position.Value.X;
        int direction = x < AutoScrollEdgeThreshold ? -1
            : x > ChannelsCollectionView.Width - AutoScrollEdgeThreshold ? 1
            : 0;

        if (direction == 0)
        {
            StopAutoScroll();
            return;
        }

        // Déjà en cours dans la même direction : on laisse le timer progresser tout seul plutôt
        // que de repartir de l'item actuellement survolé (cf. commentaire sur OnChannelDragLeave).
        if (_autoScrollTimer != null && _autoScrollDirection == direction)
            return;

        StopAutoScroll();
        _autoScrollDirection = direction;
        _autoScrollIndex = _vm.CurrentChannels.IndexOf(hovered);

        _autoScrollTimer = Dispatcher.CreateTimer();
        _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(400);
        _autoScrollTimer.Tick += (_, _) => AdvanceAutoScroll();
        _autoScrollTimer.Start();
    }

    private void AdvanceAutoScroll()
    {
        if (_vm.CurrentChannels.Count == 0)
        {
            StopAutoScroll();
            return;
        }

        _autoScrollIndex = Math.Clamp(_autoScrollIndex + _autoScrollDirection, 0, _vm.CurrentChannels.Count - 1);
        ChannelsCollectionView.ScrollTo(_autoScrollIndex, position: ScrollToPosition.MakeVisible, animate: true);
    }

    private void StopAutoScroll()
    {
        if (_autoScrollTimer == null) return;
        _autoScrollTimer.Stop();
        _autoScrollTimer = null;
        _autoScrollDirection = 0;
    }
}
