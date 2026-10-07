namespace Resonance.Components;

public partial class RotatedLabelView : ContentView
{
    public RotatedLabelView()
    {
        InitializeComponent();
        Loaded += (_, _) => UpdateSize();
    }

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(
            nameof(Text),
            typeof(string),
            typeof(RotatedLabelView),
            string.Empty,
            propertyChanged: (bindable, _, _) => ((RotatedLabelView)bindable).UpdateSize());

    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(
            nameof(TextColor),
            typeof(Color),
            typeof(RotatedLabelView),
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
            typeof(RotatedLabelView),
            // Cf. ChannelVolumeSliderView.xaml.cs : Resources["AppFontSizeBase"] renverrait l'objet
            // OnIdiom<double> brut (pas resolu) via un simple acces dictionnaire C#.
            defaultValueCreator: _ => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop ? 12.0 : 14.0,
            propertyChanged: (bindable, _, _) => ((RotatedLabelView)bindable).UpdateSize());

    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public static readonly BindableProperty LineBreakModeProperty =
        BindableProperty.Create(
            nameof(LineBreakMode),
            typeof(LineBreakMode),
            typeof(RotatedLabelView),
            Microsoft.Maui.LineBreakMode.NoWrap);

    public LineBreakMode LineBreakMode
    {
        get => (LineBreakMode)GetValue(LineBreakModeProperty);
        set => SetValue(LineBreakModeProperty, value);
    }

    /// <summary>Angle de rotation du texte, en degrés (par défaut vertical, de bas en haut).</summary>
    public static readonly BindableProperty RotationAngleProperty =
        BindableProperty.Create(
            nameof(RotationAngle),
            typeof(double),
            typeof(RotatedLabelView),
            -90.0);

    public double RotationAngle
    {
        get => (double)GetValue(RotationAngleProperty);
        set => SetValue(RotationAngleProperty, value);
    }

    /// <summary>
    /// Plafond au-delà duquel le texte est tronqué (LineBreakMode) plutôt que de continuer à
    /// grandir : sans ça, un nom de piste très long (ex. un nom de fichier complet) réclame toute
    /// la hauteur disponible dans la ligne "Auto" et écrase le slider voisin dans sa ligne "*".
    /// Abaissé à 90 (au lieu de 140) pour laisser plus de place au slider sur mobile.
    /// </summary>
    public static readonly BindableProperty MaximumLengthProperty =
        BindableProperty.Create(
            nameof(MaximumLength),
            typeof(double),
            typeof(RotatedLabelView),
            90.0,
            propertyChanged: (bindable, _, _) => ((RotatedLabelView)bindable).UpdateSize());

    public double MaximumLength
    {
        get => (double)GetValue(MaximumLengthProperty);
        set => SetValue(MaximumLengthProperty, value);
    }

    /// <summary>
    /// Hauteur fixée à MaximumLength (pas à la longueur réelle du texte) : dans l'AudioMixer, deux
    /// channel strips voisins avec des noms de piste de longueurs différentes ("Canal 3" vs "Grotte
    /// des Gobelins") se retrouvaient avec des lignes "nom de piste" de hauteurs différentes, ce qui
    /// décalait leurs sliders (ligne "*" au-dessus) les uns par rapport aux autres au lieu de les
    /// garder alignés à la même hauteur dans tout le mixeur.
    /// </summary>
    private void UpdateSize()
    {
        if (InnerLabel.Handler == null)
            return;

        InnerLabel.WidthRequest = MaximumLength;
        RootGrid.HeightRequest = MaximumLength;
    }
}
