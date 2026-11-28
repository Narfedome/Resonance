using Resonance.Components;
using Resonance.Models.Library;

namespace Resonance.Features.Library;

public partial class LibraryTrackView : ContentView
{
    public LibraryTrackView()
    {
        InitializeComponent();
        // Taille de la pochette carrée : estimée tout de suite d'après l'écran (le CollectionView mesure
        // sa première tuile avant d'avoir sa largeur, cf. MeasureFirstItem), puis recalculée sur la
        // largeur réelle de la liste.
        var display = DeviceDisplay.Current.MainDisplayInfo;
        if (display.Density > 0)
            UpdateTileSize(display.Width / display.Density - 32);
        ItemsCollection.SizeChanged += (_, _) => UpdateTileSize(ItemsCollection.Width);
        // Desktop : ResponsiveGridSpanBehavior change le nombre de colonnes APRÈS notre SizeChanged ;
        // on recalcule donc aussi quand Span change, sinon la tuile garde la taille d'une colonne de 3
        // et déborde de sa cellule (rognée à droite).
        if (ItemsCollection.ItemsLayout is GridItemsLayout gridLayout)
            gridLayout.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(GridItemsLayout.Span))
                    UpdateTileSize(ItemsCollection.Width);
            };
#if WINDOWS
        ItemsCollection.Loaded += OnItemsCollectionLoadedWindows;
#endif
    }

    public static readonly BindableProperty IsCrudProperty =
    BindableProperty.Create(nameof(IsCrud), typeof(bool), typeof(LibraryTrackView), default(bool));

    public bool IsCrud
    {
        get => (bool)GetValue(IsCrudProperty);
        set => SetValue(IsCrudProperty, value);
    }

    public static readonly BindableProperty TileSizeProperty =
        BindableProperty.Create(nameof(TileSize), typeof(double), typeof(LibraryTrackView), 110.0);

    /// <summary>Côté de la pochette carrée d'une tuile : la largeur d'une colonne de la grille.</summary>
    public double TileSize
    {
        get => (double)GetValue(TileSizeProperty);
        private set => SetValue(TileSizeProperty, value);
    }

    private void UpdateTileSize(double listWidth)
    {
        // Desktop : colonnes de largeur fixe (ResponsiveGridSpanBehavior, TileWidth=150) - une pochette
        // fixe qui y tient avec sa marge. Calculée sur la largeur, elle débordait de la cellule WinUI.
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            TileSize = 124;
            return;
        }

        if (listWidth <= 0 || ItemsCollection.ItemsLayout is not GridItemsLayout grid)
            return;

        var span = Math.Max(1, grid.Span);
        var size = Math.Floor((listWidth - (span - 1) * grid.HorizontalItemSpacing) / span) - 1;
        // Desktop : la largeur de colonne est déjà bornée par ResponsiveGridSpanBehavior (TileWidth).
        size = Math.Clamp(size, 80, 180);
        if (Math.Abs(size - TileSize) >= 1)
            TileSize = size;
    }

    // Appui long sur une pochette : entre en mode sélection, mais uniquement en mode CRUD - dans le
    // sélecteur de piste du mixeur (IsCrud=False), cocher plusieurs pistes n'aurait aucun sens.
    public System.Windows.Input.ICommand? TileLongPressCommand =>
        IsCrud && BindingContext is LibraryTrackViewModel vm ? vm.LongPressItemCommand : null;

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsCrud))
            base.OnPropertyChanged(nameof(TileLongPressCommand));
    }

    protected override void OnBindingContextChanged()
    {
        base.OnBindingContextChanged();
        base.OnPropertyChanged(nameof(TileLongPressCommand));
    }

    // RemainingItemsThresholdReachedCommand est peu fiable sur certaines plateformes (notamment quand la vue
    // est imbriquée dans un ControlTemplate comme WatermarkedLayout) : on détecte la fin de liste manuellement.
    // Fonctionne sur Android/iOS/MacCatalyst. Sur Windows, le ScrollViewer natif sous-jacent au
    // CollectionView n'est jamais retrouvé par le handler MAUI (bug WinUI connu, cf.
    // dotnet/maui#12725 et #15914) : Scrolled ne s'y déclenche donc jamais, d'où le hook natif
    // séparé ci-dessous pour cette seule plateforme (cf. #if WINDOWS).
    private void OnCollectionViewScrolled(object? sender, ItemsViewScrolledEventArgs e)
    {
        if (e.LastVisibleItemIndex < 0 || BindingContext is not LibraryTrackViewModel vm)
            return;

        // ItemsViewScrolledEventArgs n'expose pas d'index par groupe (pas de LastVisibleItemGroupIndex
        // dans cette version de .NET MAUI) : LastVisibleItemIndex reste comparé au nombre total de
        // pistes chargées tous groupes confondus (LoadedTrackCount), pas au nombre de groupes
        // (TrackItems.Count, une catégorie par groupe).
        if (e.LastVisibleItemIndex >= vm.LoadedTrackCount - 3 && vm.LoadMoreTracksCommand.CanExecute(null))
        {
            vm.LoadMoreTracksCommand.Execute(null);
        }
    }

#if WINDOWS
    private Microsoft.UI.Xaml.Controls.ScrollViewer? _nativeScrollViewer;
    private int _hookAttempts;

    private void OnItemsCollectionLoadedWindows(object? sender, EventArgs e)
    {
        _hookAttempts = 0;
        ScheduleHookAttempt();

        // Bascule plein écran / redimensionnement : la page chargée peut ne plus suffire à remplir
        // le viewport agrandi, auquel cas il n'y a plus rien à scroller pour déclencher un chargement
        // (ScrollableHeight reste à 0). On ne réagit pas à la taille intermédiaire remontée pendant
        // l'animation elle-même : SizeChanged ne sert ici que de simple déclencheur pour revérifier,
        // une fois le layout stabilisé, la mesure réelle exposée par le ScrollViewer natif.
        // -= puis += : Loaded peut se redéclencher sur la même instance (ex. retour de navigation),
        // ce qui empilerait sinon un abonnement supplémentaire à chaque fois.
        ItemsCollection.SizeChanged -= OnItemsCollectionSizeChangedWindows;
        ItemsCollection.SizeChanged += OnItemsCollectionSizeChangedWindows;
    }

    private void OnItemsCollectionSizeChangedWindows(object? sender, EventArgs e) =>
        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(250), () => ScheduleViewportFillCheck());

    private void ScheduleHookAttempt()
    {
        if (TryHookNativeScrollViewer())
        {
            ScheduleViewportFillCheck();
            return;
        }

        // Le template natif n'est pas forcément réalisé au moment de Loaded : quelques tentatives
        // espacées suffisent à couvrir le délai (constaté : plusieurs centaines de ms sur Windows).
        _hookAttempts++;
        if (_hookAttempts >= 20)
            return;

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(200), ScheduleHookAttempt);
    }

    // Charge des pages supplémentaires tant que le contenu chargé ne remplit pas le viewport (donc
    // rien à scroller pour déclencher un chargement autrement) : LoadMoreTracksCommand est un no-op
    // sûr une fois la bibliothèque épuisée (cf. _hasMoreItems dans le ViewModel), la borne d'essais
    // n'est là que pour éviter de vérifier indéfiniment si jamais ScrollableHeight ne bouge jamais.
    private void ScheduleViewportFillCheck(int attemptsLeft = 10)
    {
        if (_nativeScrollViewer == null || BindingContext is not LibraryTrackViewModel vm)
            return;

        if (_nativeScrollViewer.ScrollableHeight > 0)
            return;

        if (vm.LoadMoreTracksCommand.CanExecute(null))
            vm.LoadMoreTracksCommand.Execute(null);

        if (attemptsLeft <= 0)
            return;

        Dispatcher.DispatchDelayed(TimeSpan.FromMilliseconds(300), () => ScheduleViewportFillCheck(attemptsLeft - 1));
    }

    private bool TryHookNativeScrollViewer()
    {
        if (_nativeScrollViewer != null)
            return true;

        if (ItemsCollection.Handler?.PlatformView is not Microsoft.UI.Xaml.DependencyObject platformView)
            return false;

        _nativeScrollViewer = FindDescendant<Microsoft.UI.Xaml.Controls.ScrollViewer>(platformView);
        if (_nativeScrollViewer == null)
            return false;

        _nativeScrollViewer.ViewChanged += OnNativeScrollViewerViewChanged;
        return true;
    }

    private void OnNativeScrollViewerViewChanged(object? sender, Microsoft.UI.Xaml.Controls.ScrollViewerViewChangedEventArgs e)
    {
        if (sender is not Microsoft.UI.Xaml.Controls.ScrollViewer scrollViewer || BindingContext is not LibraryTrackViewModel vm)
            return;

        const double thresholdPixels = 400;
        bool nearBottom = scrollViewer.VerticalOffset >= scrollViewer.ScrollableHeight - thresholdPixels;
        if (nearBottom && vm.LoadMoreTracksCommand.CanExecute(null))
        {
            vm.LoadMoreTracksCommand.Execute(null);
        }
    }

    private static T? FindDescendant<T>(Microsoft.UI.Xaml.DependencyObject root) where T : Microsoft.UI.Xaml.DependencyObject
    {
        int count = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(root, i);
            if (child is T target)
                return target;

            var descendant = FindDescendant<T>(child);
            if (descendant != null)
                return descendant;
        }

        return null;
    }
#endif
}
