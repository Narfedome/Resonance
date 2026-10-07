using System.Windows.Input;

namespace Resonance.Components;

/// <summary>Icône + texte comme une seule zone tappable (cf. FaIconButtonView pour l'équivalent icône seule).</summary>
public partial class IconTextButton : ContentView
{
    public event EventHandler? Clicked;

    public IconTextButton()
    {
        InitializeComponent();
    }

    protected override void OnPropertyChanged(string? propertyName = null)
    {
        base.OnPropertyChanged(propertyName);
        if (propertyName == nameof(IsEnabled))
            VisualStateManager.GoToState(RootLayout, IsEnabled ? "Normal" : "Disabled");
    }

    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(IconTextButton), default(string));
    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly BindableProperty IconFontProperty =
        BindableProperty.Create(nameof(IconFont), typeof(string), typeof(IconTextButton), "FontSolid");
    public string IconFont
    {
        get => (string)GetValue(IconFontProperty);
        set => SetValue(IconFontProperty, value);
    }

    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(IconTextButton), 14.0);
    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }

    public static readonly BindableProperty TextProperty =
        BindableProperty.Create(nameof(Text), typeof(string), typeof(IconTextButton), default(string));
    public string Text
    {
        get => (string)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public static readonly BindableProperty TextColorProperty =
        BindableProperty.Create(nameof(TextColor), typeof(Color), typeof(IconTextButton), Colors.Black);
    public Color TextColor
    {
        get => (Color)GetValue(TextColorProperty);
        set => SetValue(TextColorProperty, value);
    }

    // Non contraint par défaut (même valeur que le défaut MAUI de MaximumWidthRequest) : n'affecte
    // aucun usage existant tant qu'on ne le fixe pas explicitement (cf. TrackEditDialog.FileButton,
    // dont le nom de fichier peut largement dépasser la largeur de la carte).
    public static readonly BindableProperty TextMaximumWidthProperty =
        BindableProperty.Create(nameof(TextMaximumWidth), typeof(double), typeof(IconTextButton), double.PositiveInfinity);
    public double TextMaximumWidth
    {
        get => (double)GetValue(TextMaximumWidthProperty);
        set => SetValue(TextMaximumWidthProperty, value);
    }

    public static readonly BindableProperty FontSizeProperty =
        // Cf. ChannelVolumeSliderView.xaml.cs : Resources["AppFontSizeBase"] renverrait l'objet
        // OnIdiom<double> brut (pas resolu) via un simple acces dictionnaire C#.
        BindableProperty.Create(nameof(FontSize), typeof(double), typeof(IconTextButton),
            defaultValueCreator: (BindableObject _) => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop ? 12.0 : 14.0);
    public double FontSize
    {
        get => (double)GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public static readonly BindableProperty SpacingProperty =
        BindableProperty.Create(nameof(Spacing), typeof(double), typeof(IconTextButton), 8.0);
    public double Spacing
    {
        get => (double)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(IconTextButton));
    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(IconTextButton));
    public object? CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }

    // IsEnabled cascade normalement l'input aux enfants, mais un TapGestureRecognizer nu (pas un
    // vrai Button) n'est pas toujours bloqué en pratique selon la plateforme — on vérifie donc
    // explicitement plutôt que de compter dessus.
    void OnTapped(object? sender, TappedEventArgs e)
    {
        if (!IsEnabled) return;

        _ = PulseAsync();
        Clicked?.Invoke(this, EventArgs.Empty);
        if (Command?.CanExecute(CommandParameter) == true)
            Command.Execute(CommandParameter);
    }

    // Retour visuel au tap (le style Button implicite l'offrait via ses VisualState Pressed) : un
    // TapGestureRecognizer nu n'a pas d'événement "pressed" séparé du "released" sur toutes les
    // plateformes, donc on rejoue l'effet après coup plutôt que pendant l'appui.
    async Task PulseAsync()
    {
        await RootLayout.ScaleToAsync(0.97, 60, Easing.CubicOut);
        await RootLayout.ScaleToAsync(1.0, 90, Easing.CubicOut);
    }

    // Survol souris (desktop uniquement — le tactile Android n'émet pas ces événements, il reste
    // couvert par le pulse au tap ci-dessus).
    void OnPointerEntered(object? sender, PointerEventArgs e)
    {
        if (IsEnabled) VisualStateManager.GoToState(RootLayout, "PointerOver");
    }

    void OnPointerExited(object? sender, PointerEventArgs e)
    {
        if (IsEnabled) VisualStateManager.GoToState(RootLayout, "Normal");
    }
}
