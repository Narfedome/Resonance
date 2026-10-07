using CommunityToolkit.Maui.Extensions;

namespace Resonance.Features.Campaigns;

public partial class CampaignPage : ContentPage
{
    private readonly CampaignViewModel _vm;
    private bool _initialized;

    // Cf. AudioMixerPage pour le principe général (poignée dédiée, DropCompleted pas fiable sur
    // Android...). Ici en plus : léger décalage/estompage de la ligne glissée, et encadré doré
    // superposé (DragHighlightBorder, nommé dans chaque gabarit) sur la ligne SURVOLÉE
    // uniquement (cible d'échange) — pas sur la ligne glissée elle-même, jugé superflu.
    private VisualElement? _draggingElement;

    private static Color AccentColor => (Color)Application.Current!.Resources["AppAccent"];
    private static Color HighlightBackgroundColor => AccentColor.WithAlpha(0.12f);

    public CampaignPage(CampaignViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        // Une popup thémée qui se ferme redéclenche la navigation, mais le ViewModel a déjà mis à
        // jour Rows localement (Create/Rename/Delete) — pas besoin de tout recharger. De même, revenir
        // du Mixer (changement d'onglet) ne doit pas réinitialiser l'arborescence dépliée : seul le
        // tout premier affichage charge les campagnes.
        if (args.WasPreviousPageACommunityToolkitPopupPage() || _initialized)
            return;

        _initialized = true;
        await _vm.InitializeAsync();
    }

    // Cf. SceneDataService.ReorderAsync : ne stocke que l'item glissé, pas un index, pour rester
    // valide même si l'ordre a changé entre le début du drag et le drop.
    private void OnRowDragStarting(object sender, DragStartingEventArgs e)
    {
        if (sender is not VisualElement { BindingContext: ExplorerRow row } grip)
            return;

        e.Data.Properties["Row"] = row;

        // Un sous-arbre déplié ne suit pas la ligne glissée : on le replie d'abord (cf.
        // CollapseRowForDrag) pour ne pas l'éparpiller par le déplacement.
        _vm.CollapseRowForDrag(row);

        _draggingElement = FindGestureRoot(grip);
        _draggingElement.Opacity = 0.55;
        _draggingElement.TranslationX = 10;
    }

    // Part de start.Parent, PAS de start : la poignée elle-même porte déjà un DragGestureRecognizer
    // (donc GestureRecognizers.Count > 0), en la testant on la retrouvait immédiatement au lieu de
    // remonter jusqu'à la vraie racine (celle qui porte le DropGestureRecognizer) — c'est ce qui
    // faisait bouger/estomper juste la poignée au lieu de toute la ligne.
    private static VisualElement FindGestureRoot(VisualElement start)
    {
        Element? current = start.Parent;
        while (current != null && current is not View { GestureRecognizers.Count: > 0 })
            current = current.Parent;
        return current as VisualElement ?? start;
    }

    // DragHighlightBorder est un Border transparent superposé à chaque ligne (cf. les 3 gabarits
    // dans CampaignPage.xaml) : contrairement au Border racine de CampaignTemplate (dont le Stroke
    // est déjà piloté par IsExpanded), celui-ci n'est utilisé QUE pour ce feedback, jamais en
    // conflit avec un autre binding. FindByName cherche dans le namescope de CETTE instance de
    // DataTemplate réalisée par la CollectionView (pas globalement dans la page).
    private static void SetHighlightBorder(VisualElement root, Color stroke, Color background)
    {
        if (root.FindByName<Border>("DragHighlightBorder") is not Border border) return;
        border.Stroke = stroke;
        border.BackgroundColor = background;
    }

    private void OnRowDropCompleted(object sender, DropCompletedEventArgs e) => ResetDraggingState();

    private void OnRowDragOver(object sender, DragEventArgs e)
    {
        if (sender is VisualElement element)
            SetHighlightBorder(element, AccentColor, HighlightBackgroundColor);
    }

    private void OnRowDragLeave(object sender, DragEventArgs e)
    {
        if (sender is VisualElement element)
            SetHighlightBorder(element, Colors.Transparent, Colors.Transparent);
    }

    private async void OnRowDrop(object sender, DropEventArgs e)
    {
        if (sender is VisualElement targetElement)
            SetHighlightBorder(targetElement, Colors.Transparent, Colors.Transparent);
        ResetDraggingState();

        if (sender is not Element { BindingContext: ExplorerRow target })
            return;
        if (!e.Data.Properties.TryGetValue("Row", out var draggedObj) || draggedObj is not ExplorerRow dragged)
            return;

        await _vm.ReorderRowsAsync(dragged, target);
    }

    private void ResetDraggingState()
    {
        if (_draggingElement == null) return;
        _draggingElement.Opacity = 1;
        _draggingElement.TranslationX = 0;
        _draggingElement = null;
    }
}
