using Microsoft.Maui.Layouts;
using Resonance.Components;
using Resonance.Services;

namespace Resonance.Features.AudioMixer;

public partial class AudioMixerPage : ContentPage
{
    private readonly AudioMixerViewModel _vm;

    private static Color AccentColor => (Color)Application.Current!.Resources["AppAccent"];
    private static Color HighlightBackgroundColor => AccentColor.WithAlpha(0.12f);

    // Auto-scroll pendant le drag : une zone qui déborde ne défile pas toute seule quand on approche
    // de son bord. On avance d'un pas par tick tant que le pointeur reste près du bord.
    private const double AutoScrollEdgeThreshold = 60;
    private const double AutoScrollStep = 90;
    private IDispatcherTimer? _autoScrollTimer;
    private int _autoScrollDirection;
    private ScrollView? _autoScrollView;

    public AudioMixerPage(AudioMixerViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        // Sur Windows, assez de place pour les ambiances et les effets côte à côte, sans onglets.
        _vm.IsSideBySide = DeviceInfo.Idiom == DeviceIdiom.Desktop;
        if (_vm.IsSideBySide)
        {
            // Ambiances | trait vertical | effets.
            ChannelsArea.ColumnDefinitions = new ColumnDefinitionCollection
            {
                new ColumnDefinition(new GridLength(1.15, GridUnitType.Star)),
                new ColumnDefinition(GridLength.Auto),
                new ColumnDefinition(GridLength.Star)
            };
            Grid.SetColumn(ColumnDivider, 1);
            Grid.SetColumn(EffectsPanel, 2);
        }
        else
        {
            // Sur téléphone, le bouton flottant de la scène libre (en bas à droite) recouvrait
            // l'étiquette de la dernière tranche visible : on lui réserve sa hauteur sous les tranches.
            AmbienceScroll.Margin = new Thickness(0, 0, 0, 64);
        }

        // Pads sur 2 colonnes (3 côte à côte sous Windows) : chaque pad prend sa part de la largeur.
        var padBasis = new FlexBasis(_vm.IsSideBySide ? 1f / 3 : 0.5f, isRelative: true);
        EffectsFlex.ChildAdded += (_, e) =>
        {
            if (e.Element is BindableObject pad)
                FlexLayout.SetBasis(pad, padBasis);
        };

        _vm.PropertyChanged += (_, e) =>
        {
            // IsEffectsTab : le canal sélectionné vient de changer de famille (et l'onglet l'a suivi).
            if (e.PropertyName is nameof(AudioMixerViewModel.SelectedChannel) or nameof(AudioMixerViewModel.IsEffectsTab))
                ScrollToSelectedChannel();
        };
    }

    // Vue affichée (tranche ou pad) d'un canal, parmi les enfants de sa rangée ou de sa grille.
    private VisualElement? ViewOf(ChannelStripViewModel channel)
    {
        Layout container = channel.IsOneShot ? EffectsFlex : AmbienceStack;
        return container.Children.OfType<VisualElement>().FirstOrDefault(v => ReferenceEquals(v.BindingContext, channel));
    }

    // Un canal tout juste ajouté, ou passé d'ambiance à effet (ou l'inverse) depuis ses réglages,
    // peut se retrouver hors de l'écran : on fait défiler sa zone jusqu'à lui.
    private void ScrollToSelectedChannel()
    {
        if (_vm.SelectedChannel is not ChannelStripViewModel channel)
            return;

        var scroll = channel.IsOneShot ? EffectsScroll : AmbienceScroll;
        Dispatcher.Dispatch(async () =>
        {
            if (ViewOf(channel) is VisualElement view)
                await scroll.ScrollToAsync(view, ScrollToPosition.MakeVisible, animated: true);
        });
    }
#if WINDOWS
    // Raccourcis clavier du mixeur (Ctrl+flèches, Ctrl+Entrée...), cf. Platforms/Windows/MixerShortcuts.
    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();
        if (Handler?.PlatformView is Microsoft.UI.Xaml.UIElement element)
            Platforms.Windows.MixerShortcuts.Attach(element, _vm);
    }
#endif

    // Rafraîchit la barre de progression des canaux en lecture, seulement tant que le mixeur est
    // affiché : le lecteur ne notifie pas sa position, il faut la relire.
    private IDispatcherTimer? _progressTimer;

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _progressTimer?.Stop();
    }

    private void StartProgressTimer()
    {
        if (_progressTimer == null)
        {
            _progressTimer = Dispatcher.CreateTimer();
            _progressTimer.Interval = TimeSpan.FromMilliseconds(250);
            _progressTimer.Tick += (_, _) =>
            {
                foreach (var channel in _vm.CurrentChannels)
                {
                    if (channel.IsPlaying && channel.ShowsProgress)
                        channel.RefreshProgress();
                }
            };
        }
        _progressTimer.Start();
    }

    // Ouvrir le Mixer sans rien avoir chargé (onglet toujours visible, cf. AppShell) affichait un
    // état vide avec les boutons "ajouter" désactivés (HasActiveScene) sans indiquer comment en
    // sortir. Charger la scène orpheline automatiquement dans ce cas précis - jamais si une scène
    // est déjà active, ni si CampaignViewModel.Launch est en train de charger une vraie scène (cf.
    // SuppressNextFreeformAutoLoad, positionné avant la navigation - pas de race possible ici).
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        StartProgressTimer();

        if (_vm.SuppressNextFreeformAutoLoad)
        {
            _vm.SuppressNextFreeformAutoLoad = false;
            return;
        }

        if (!_vm.HasActiveScene)
            await UiErrors.RunAsync(_vm.SelectFreeformScene, "AudioMixerPage.OnAppearing");
    }

    // DragHighlightBorder est un Border transparent superposé à chaque tranche/pad (cf. XAML),
    // indépendant des repères du canal lui-même (sélection, lecture). FindByName cherche dans le
    // namescope de CETTE instance de DataTemplate.
    private static void SetHighlightBorder(VisualElement root, Color stroke, Color background)
    {
        if (root.FindByName<Border>("DragHighlightBorder") is not Border border) return;
        border.Stroke = stroke;
        border.BackgroundColor = background;
    }

    private void OnChannelDragOver(object? sender, DragEventArgs e)
    {
        if (sender is not VisualElement { BindingContext: ChannelStripViewModel hovered } element)
            return;

        SetHighlightBorder(element, AccentColor, HighlightBackgroundColor);
        UpdateAutoScroll(e, hovered);
    }

    private void OnChannelDragLeave(object? sender, DragEventArgs e)
    {
        if (sender is VisualElement element)
            SetHighlightBorder(element, Colors.Transparent, Colors.Transparent);
        // L'auto-scroll n'est PAS arrêté ici : ce handler se déclenche aussi en passant d'un
        // canal à son voisin (juste avant le DragOver du suivant), l'arrêter à chaque fois ferait
        // repartir la progression de zéro à chaque item traversé.
    }

    private async void OnChannelDrop(object? sender, DropEventArgs e)
    {
        if (sender is VisualElement targetElement)
            SetHighlightBorder(targetElement, Colors.Transparent, Colors.Transparent);
        // Filet de sécurité : sur Android, DropCompleted (côté source du drag) ne se déclenche pas
        // toujours de façon fiable (bug connu dotnet/maui#17554).
        ChannelDrag.Reset();
        StopAutoScroll();

        if (sender is not Element { BindingContext: ChannelStripViewModel target })
            return;
        if (!e.Data.Properties.TryGetValue("Channel", out var draggedObj) || draggedObj is not ChannelStripViewModel dragged)
            return;
        // Une ambiance ne se range pas parmi les effets (et inversement) : sur Windows, les deux
        // listes sont côte à côte et un drop de l'une sur l'autre est possible.
        if (dragged.IsOneShot != target.IsOneShot)
            return;

        await UiErrors.RunAsync(() => _vm.ReorderChannelsAsync(dragged, target), "AudioMixerPage.OnChannelDrop");
    }

    private void UpdateAutoScroll(DragEventArgs e, ChannelStripViewModel hovered)
    {
        // Les tranches d'ambiance défilent horizontalement, les pads d'effet verticalement.
        var scroll = hovered.IsOneShot ? EffectsScroll : AmbienceScroll;
        bool horizontal = !hovered.IsOneShot;
        var position = e.GetPosition(scroll);
        double length = horizontal ? scroll.Width : scroll.Height;
        if (position is null || length <= 0)
        {
            StopAutoScroll();
            return;
        }

        double p = horizontal ? position.Value.X : position.Value.Y;
        int direction = p < AutoScrollEdgeThreshold ? -1
            : p > length - AutoScrollEdgeThreshold ? 1
            : 0;

        if (direction == 0)
        {
            StopAutoScroll();
            return;
        }

        // Déjà en cours dans la même direction : on laisse le timer progresser tout seul (cf.
        // commentaire sur OnChannelDragLeave).
        if (_autoScrollTimer != null && _autoScrollDirection == direction && ReferenceEquals(_autoScrollView, scroll))
            return;

        StopAutoScroll();
        _autoScrollView = scroll;
        _autoScrollDirection = direction;

        _autoScrollTimer = Dispatcher.CreateTimer();
        _autoScrollTimer.Interval = TimeSpan.FromMilliseconds(250);
        _autoScrollTimer.Tick += async (_, _) => await AdvanceAutoScrollAsync();
        _autoScrollTimer.Start();
    }

    private async Task AdvanceAutoScrollAsync()
    {
        if (_autoScrollView is not ScrollView scroll)
            return;

        if (scroll.Orientation == ScrollOrientation.Horizontal)
        {
            double max = Math.Max(0, scroll.ContentSize.Width - scroll.Width);
            await scroll.ScrollToAsync(Math.Clamp(scroll.ScrollX + _autoScrollDirection * AutoScrollStep, 0, max), 0, animated: true);
        }
        else
        {
            double max = Math.Max(0, scroll.ContentSize.Height - scroll.Height);
            await scroll.ScrollToAsync(0, Math.Clamp(scroll.ScrollY + _autoScrollDirection * AutoScrollStep, 0, max), animated: true);
        }
    }

    private void StopAutoScroll()
    {
        if (_autoScrollTimer == null) return;
        _autoScrollTimer.Stop();
        _autoScrollTimer = null;
        _autoScrollDirection = 0;
        _autoScrollView = null;
    }
}