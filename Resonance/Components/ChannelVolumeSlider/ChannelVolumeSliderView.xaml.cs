namespace Resonance.Components;

public partial class ChannelVolumeSliderView : ContentView
{
    public ChannelVolumeSliderView()
    {
        InitializeComponent();

#if WINDOWS
        // Sur Windows, InnerSlider est bascule sur l'orientation verticale native de WinUI (cf.
        // MauiProgram.cs, mapping "VerticalOrientation" scope via StyleId) plutot que tourne de
        // -90° comme sur les autres plateformes : evite un bug de redessin WinUI (confirme en
        // testant sans cette bascule - le bug etait bien revenu, independant du souci de couleur du
        // thumb, cf. mapping "AccentThumbBrush" qui couvre tous les Slider quelle que soit leur
        // orientation). VerticalOptions=Fill lui fait occuper toute la hauteur de TrackHost (la
        // longueur de la piste). HorizontalOptions=Center (pas Fill) : le template natif du Slider
        // vertical de WinUI ne centre pas son thumb/track a l'interieur de bounds etirees a la
        // largeur de la colonne (50px) - le laisser se dimensionner a sa largeur naturelle et se
        // centrer lui-meme donne un rendu aligne, comme sur les autres plateformes.
        InnerSlider.Rotation = 0;
        InnerSlider.HorizontalOptions = LayoutOptions.Center;
        InnerSlider.VerticalOptions = LayoutOptions.Fill;
#else
        TrackHost.SizeChanged += (_, _) => UpdateTrackLength();

        // À l'intérieur du CollectionView (item template), le tout premier passage de layout
        // mesure souvent la ligne "*" avec une contrainte non résolue (elle reste à 0) - le
        // slider n'apparaît qu'après un évènement de resize qui force un nouveau passage. On
        // force donc nous-même un remeasure une fois la vue montée, une fois le dispatcher UI
        // libre (le layout de la CollectionView est alors retombé sur ses pieds).
        Loaded += (_, _) => Dispatcher.Dispatch(() => TrackHost.InvalidateMeasure());
#endif
    }

#if !WINDOWS
    /// <summary>
    /// TrackHost (ligne "*") reçoit la hauteur que le Grid parent lui accorde ; on la reporte
    /// comme WidthRequest du slider avant rotation pour qu'il remplisse exactement cet espace,
    /// au lieu d'une longueur fixe qui déborde ou laisse un vide selon la taille de la fenêtre.
    /// </summary>
    private void UpdateTrackLength()
    {
        if (TrackHost.Height > 0)
            InnerSlider.WidthRequest = TrackHost.Height;
    }
#endif

    public static readonly BindableProperty ValueProperty =
        BindableProperty.Create(
            nameof(Value),
            typeof(double),
            typeof(ChannelVolumeSliderView),
            0.0,
            BindingMode.TwoWay);

    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public static readonly BindableProperty MinimumProperty =
        BindableProperty.Create(
            nameof(Minimum),
            typeof(double),
            typeof(ChannelVolumeSliderView),
            0.0);

    public double Minimum
    {
        get => (double)GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public static readonly BindableProperty MaximumProperty =
        BindableProperty.Create(
            nameof(Maximum),
            typeof(double),
            typeof(ChannelVolumeSliderView),
            1.0);

    public double Maximum
    {
        get => (double)GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>Texte affiché au-dessus du slider (ex. valeur formatée en pourcentage).</summary>
    public static readonly BindableProperty ValueTextProperty =
        BindableProperty.Create(
            nameof(ValueText),
            typeof(string),
            typeof(ChannelVolumeSliderView),
            string.Empty);

    public string ValueText
    {
        get => (string)GetValue(ValueTextProperty);
        set => SetValue(ValueTextProperty, value);
    }

    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(
            nameof(TextColor),
            typeof(Color),
            typeof(ChannelVolumeSliderView),
            defaultValueCreator: _ => (Color)Application.Current!.Resources["AppText"]);

    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    public static readonly BindableProperty FontSizeProperty =
        BindableProperty.Create(
            nameof(FontSize),
            typeof(double),
            typeof(ChannelVolumeSliderView),
            // Resources["AppFontSizeBase"] (Sizes.xaml, defini via <OnIdiom .../>) renverrait
            // l'objet OnIdiom<double> brut, pas le double resolu - {DynamicResource} en XAML sait
            // le "deballer", mais pas un simple acces dictionnaire C#, d'ou un cast invalide.
            // Meme valeurs que AppFontSizeBase, calculees directement via l'idiome courant.
            defaultValueCreator: _ => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop ? 12.0 : 14.0);

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    /// <summary>Angle de rotation du slider, en degrés (par défaut vertical, de bas en haut).</summary>
    public static readonly BindableProperty RotationAngleProperty =
        BindableProperty.Create(
            nameof(RotationAngle),
            typeof(double),
            typeof(ChannelVolumeSliderView),
            -90.0);

    public double RotationAngle
    {
        get => (double)GetValue(RotationAngleProperty);
        set => SetValue(RotationAngleProperty, value);
    }

    /// <summary>Largeur de la colonne (label de valeur + slider).</summary>
    public static readonly BindableProperty ThicknessProperty =
        BindableProperty.Create(
            nameof(Thickness),
            typeof(double),
            typeof(ChannelVolumeSliderView),
            50.0);

    public double Thickness
    {
        get => (double)GetValue(ThicknessProperty);
        set => SetValue(ThicknessProperty, value);
    }
}
