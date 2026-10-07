using Resonance.Services;
using Microsoft.Maui.Hosting;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Slider = Microsoft.UI.Xaml.Controls.Slider;
using SolidColorBrush = Microsoft.UI.Xaml.Media.SolidColorBrush;

namespace Resonance.Platforms.Windows;

static class WindowsHandlers
{
    public static void Configure(IMauiHandlersCollection handlers)
    {
        // Sur Windows, ListViewBase.SingleSelectionFollowsFocus vaut true par défaut : le
        // premier élément réalisé d'un CollectionView prend le focus clavier au chargement
        // et s'affiche "sélectionné" (bordure accent) sans qu'aucun tap n'ait eu lieu, alors
        // que SelectedItem reste bien null côté ViewModel — trompeur pour l'utilisateur.
        Microsoft.Maui.Controls.Handlers.Items.CollectionViewHandler.Mapper.AppendToMapping("NoFocusFollowsSelection", (handler, view) =>
        {
            if (handler.PlatformView is ListViewBase listViewBase)
                listViewBase.SingleSelectionFollowsFocus = false;
        });

        // ChannelVolumeSliderView (StyleId="ChannelVolumeSliderTrack") tournait un Slider
        // horizontal de -90° pour le rendre vertical : sur WinUI, le RenderTransform de
        // rotation ne se redessine pas correctement quand la taille change dynamiquement
        // dans l'item template d'un CollectionView (le layout MAUI est correct, mais le
        // rendu natif reste calé sur une passe antérieure). Plutôt que rustiner ce rendu,
        // on bascule ce Slider précis sur l'orientation verticale native de WinUI, qui n'a
        // pas ce problème. Scopé via StyleId pour ne pas affecter les autres Slider de
        // l'app (ChannelSettingsDialog), qui restent horizontaux.
        // Testé sans cette bascule (retour au rendu MAUI tourné, comme sur les autres
        // plateformes) : le bug de redessin est bien revenu, ce n'était pas lié au thumb
        // bleu (cf. mapping "AccentThumbBrush" ci-dessous, qui est un souci séparé et
        // s'applique de toute façon à tous les Slider, natif ou non).
        Microsoft.Maui.Handlers.SliderHandler.Mapper.AppendToMapping("VerticalOrientation", (handler, view) =>
        {
            if (view is VisualElement { StyleId: "ChannelVolumeSliderTrack" } &&
                handler.PlatformView is Slider nativeSlider)
            {
                nativeSlider.Orientation = Orientation.Vertical;
            }
        });

        // Sur Windows, TOUT Slider MAUI est en réalité rendu par le vrai contrôle natif
        // WinUI (Slider) - ce n'est pas spécifique au slider vertical de l'AudioMixer ni à
        // une bascule d'orientation. Le thumb natif Fluent Design a un disque intérieur dont
        // la couleur au survol/appui vient des ressources thème
        // SliderThumbBackgroundPointerOver/Pressed - pas de la propriété MAUI ThumbColor,
        // donc il reste au bleu d'accent système Windows par défaut, quel que soit le
        // Slider. On écrase ces ressources avec l'accent de la palette courante pour tous
        // les Slider de l'appli, et on les réapplique à chaque changement de thème (ce ne
        // sont pas des {DynamicResource} MAUI).
        Microsoft.Maui.Handlers.SliderHandler.Mapper.AppendToMapping("AccentThumbBrush", (handler, view) =>
        {
            if (handler.PlatformView is Slider nativeSlider)
            {
                ApplyAccentThumbBrush(nativeSlider);
                ThemeService.Instance.ThemeChanged += () => ApplyAccentThumbBrush(nativeSlider);
            }
        });
    }

    static void ApplyAccentThumbBrush(Slider nativeSlider)
    {
        var resources = Microsoft.Maui.Controls.Application.Current!.Resources;
        var accent = ((Microsoft.Maui.Graphics.Color)resources["AppAccent"]).ToWindowsColor();
        var surface = ((Microsoft.Maui.Graphics.Color)resources["AppSurface"]).ToWindowsColor();

        var accentBrush = new SolidColorBrush(accent);
        nativeSlider.Resources["SliderThumbBackground"] = accentBrush;
        nativeSlider.Resources["SliderThumbBackgroundPointerOver"] = accentBrush;
        nativeSlider.Resources["SliderThumbBackgroundPressed"] = accentBrush;

        // Le template Fluent par defaut dessine un disque interieur de 12px enveloppe dans un
        // anneau exterieur dont l'epaisseur de bordure vaut 0 hors high-contrast : cet anneau
        // est donc invisible (masque derriere le disque, de meme taille) et le thumb rendu se
        // resume a un simple point de 12px, discret sur une piste large. On agrandit le disque
        // et on redonne de l'epaisseur a l'anneau, rempli de la couleur de surface de l'appli,
        // pour creer un halo qui detache visuellement le thumb de la piste.
        nativeSlider.Resources["SliderInnerThumbWidth"] = 18.0;
        nativeSlider.Resources["SliderInnerThumbHeight"] = 18.0;
        nativeSlider.Resources["SliderThumbCornerRadius"] = new Microsoft.UI.Xaml.CornerRadius(12);
        nativeSlider.Resources["SliderBorderThemeThickness"] = new Microsoft.UI.Xaml.Thickness(3);
        var surfaceBrush = new SolidColorBrush(surface);
        nativeSlider.Resources["SliderOuterThumbBackground"] = surfaceBrush;
        nativeSlider.Resources["SliderThumbBorderBrush"] = surfaceBrush;

        // Changer une entree de ressource locale ne repeint pas a chaud un Slider deja affiche :
        // constate en testant un changement de theme app en cours d'execution - seul le slider
        // qu'on venait de toucher (donc qui avait transite par un vrai etat visuel Pointer/
        // Pressed) affichait la bonne couleur, les autres restaient bleus malgre la ressource
        // mise a jour. Un aller-retour IsEnabled force WinUI a re-parcourir le VisualStateManager
        // et donc a relire ces ressources, sans flash visible (aucun rendu n'a lieu entre les
        // deux lignes, toutes deux synchrones sur le thread UI).
        nativeSlider.IsEnabled = false;
        nativeSlider.IsEnabled = true;
    }
}
