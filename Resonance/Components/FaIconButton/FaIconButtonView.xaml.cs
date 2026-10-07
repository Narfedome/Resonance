using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using System.Windows.Input;

namespace Resonance.Components;

public partial class FaIconButtonView : ContentView
{
    public FaIconButtonView()
    {
        InitializeComponent();
    }
    public static readonly BindableProperty BackgroundImageProperty =
    BindableProperty.Create(nameof(BackgroundImage), typeof(ImageSource), typeof(FaIconButtonView), default(ImageSource));

    public ImageSource BackgroundImage
    {
        get => (ImageSource)GetValue(BackgroundImageProperty);
        set => SetValue(BackgroundImageProperty, value);
    }

    // ICON (glyph)
    public static readonly BindableProperty IconProperty =
        BindableProperty.Create(nameof(Icon), typeof(string), typeof(FaIconButtonView), default(string));

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    // COMMAND
    public static readonly BindableProperty CommandProperty =
        BindableProperty.Create(nameof(Command), typeof(ICommand), typeof(FaIconButtonView));
    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }
    // TOOLTIP
    public static readonly BindableProperty TooltipProperty =
        BindableProperty.Create(nameof(Tooltip), typeof(string), typeof(FaIconButtonView));
    public string Tooltip
    {
        get => (string)GetValue(TooltipProperty);
        set => SetValue(TooltipProperty, value);
    }


    // COMMAND PARAM
    public static readonly BindableProperty CommandParameterProperty =
        BindableProperty.Create(nameof(CommandParameter), typeof(object), typeof(FaIconButtonView));

    public object CommandParameter
    {
        get => GetValue(CommandParameterProperty);
        set => SetValue(CommandParameterProperty, value);
    }
    // ICON SIZE
    public static readonly BindableProperty IconSizeProperty =
        BindableProperty.Create(nameof(IconSize), typeof(double), typeof(FaIconButtonView),
            // Cf. ChannelVolumeSliderView.xaml.cs pour la raison du calcul direct via DeviceInfo
            // plutot qu'un lookup dans Resources (renverrait l'objet OnIdiom brut, pas resolu).
            defaultValueCreator: _ => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop ? 14.0 : 16.0);

    public double IconSize
    {
        get => (double)GetValue(IconSizeProperty);
        set => SetValue(IconSizeProperty, value);
    }
    // ICON COLOR
    public static readonly BindableProperty IconColorProperty =
        BindableProperty.Create(nameof(IconColor), typeof(Color), typeof(FaIconButtonView), Colors.White);

    public Color IconColor
    {
        get => (Color)GetValue(IconColorProperty);
        set => SetValue(IconColorProperty, value);
    }

    // ICON BACKGROUND COLOR : defaut Transparent ici uniquement en repli statique - le vrai defaut
    // (accent plein, cf. usages Ajouter/Editer/Supprimer) vient du Style implicite TargetType=
    // FaIconButtonView dans Styles.xaml, via {DynamicResource AppAccent}. Un defaultValueCreator
    // ne peut que figer AppAccent une seule fois (a la creation du controle) : le bouton ne suivait
    // alors plus les changements de theme/palette apres coup - contrairement a l'ancien
    // BackgroundColor="{DynamicResource AppAccent}" en dur que cette propriete a remplace.
    public static readonly BindableProperty IconBackgroundColorProperty =
        BindableProperty.Create(nameof(IconBackgroundColor), typeof(Color), typeof(FaIconButtonView), Colors.Transparent);

    public Color IconBackgroundColor
    {
        get => (Color)GetValue(IconBackgroundColorProperty);
        set => SetValue(IconBackgroundColorProperty, value);
    }

    // ICON FONT
    public static readonly BindableProperty IconFontProperty =
        BindableProperty.Create(nameof(IconFont), typeof(string), typeof(FaIconButtonView), "FontSolid");

    public string IconFont
    {
        get => (string)GetValue(IconFontProperty);
        set => SetValue(IconFontProperty, value);
    }

    // CORNER RADIUS
    public static readonly BindableProperty CornerRadiusProperty =
        BindableProperty.Create(nameof(CornerRadius), typeof(int), typeof(FaIconButtonView), 8);

    public int CornerRadius
    {
        get => (int)GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    // BUTTON WIDTH/HEIGHT : carre fixe par defaut (comportement d'origine, cf. usages existants type
    // Ajouter/Editer/Supprimer ou TrackButtonView - la ou FaIconButtonView occupe une cellule avec
    // de la place en trop, ex. une pochette carree, un HeightRequest non fixe s'etirerait en barre
    // au lieu de rester une icone carree).
    // Passer ButtonHeightRequest="-1" (Unset, cf. VerticalOptions="Fill" ci-dessous dans le XAML)
    // pour laisser le bouton s'etirer jusqu'a la hauteur de sa cellule au lieu de rester carre -
    // uniquement pour un vrai remplacement du pattern Grid-de-tap-a-taille-de-ligne (cf. chevrons
    // d'accordeon CampaignPage), pas le comportement par defaut.
    public static readonly BindableProperty ButtonWidthRequestProperty =
        BindableProperty.Create(nameof(ButtonWidthRequest), typeof(double), typeof(FaIconButtonView),
            // Resources["AppMinTouchTarget"] renverrait l'objet OnIdiom<double> brut, pas resolu
            // (meme piege que IconSize ci-dessus) : on reproduit sa valeur (Sizes.xaml) directement.
            defaultValueCreator: _ => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop ? 32.0 : 44.0);

    public double ButtonWidthRequest
    {
        get => (double)GetValue(ButtonWidthRequestProperty);
        set => SetValue(ButtonWidthRequestProperty, value);
    }

    public static readonly BindableProperty ButtonHeightRequestProperty =
        BindableProperty.Create(nameof(ButtonHeightRequest), typeof(double), typeof(FaIconButtonView),
            defaultValueCreator: _ => DeviceInfo.Current.Idiom == DeviceIdiom.Desktop ? 32.0 : 44.0);

    public double ButtonHeightRequest
    {
        get => (double)GetValue(ButtonHeightRequestProperty);
        set => SetValue(ButtonHeightRequestProperty, value);
    }

    // Defaut Center, pas Fill : l'implementation d'origine (avant l'ajout de ButtonHeightRequest)
    // n'avait aucun VerticalOptions explicite sur le Button, et Fill + une taille fixe (44/32) rend
    // mal sur WinUI (icone qui flotte au lieu de rester centree). Passer "Fill" uniquement avec
    // ButtonHeightRequest="-1" pour un vrai etirement (cf. chevrons d'accordeon CampaignPage).
    public static readonly BindableProperty ButtonVerticalOptionsProperty =
        BindableProperty.Create(nameof(ButtonVerticalOptions), typeof(LayoutOptions), typeof(FaIconButtonView), LayoutOptions.Center);

    public LayoutOptions ButtonVerticalOptions
    {
        get => (LayoutOptions)GetValue(ButtonVerticalOptionsProperty);
        set => SetValue(ButtonVerticalOptionsProperty, value);
    }
}