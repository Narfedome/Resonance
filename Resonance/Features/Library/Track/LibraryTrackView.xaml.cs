using Resonance.Components;
using Resonance.Models.Library;

namespace Resonance.Features.Library;

public partial class LibraryTrackView : ContentView
{
    public LibraryTrackView()
    {
        InitializeComponent();
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
