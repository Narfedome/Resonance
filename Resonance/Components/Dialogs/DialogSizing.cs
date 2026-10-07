using Microsoft.Maui.Devices;

namespace Resonance.Components.Dialogs;

/// <summary>
/// Une fraction fixe (desktop) de la hauteur d'écran dépassait largement l'espace restant au-dessus
/// du clavier sur téléphone (le clavier + sa barre de suggestions prennent facilement 35-45% de
/// l'écran) : on calcule plutôt une fraction de la hauteur réelle de l'appareil.
/// </summary>
internal static class DialogSizing
{
    public static double MaxContentHeight()
    {
        var display = DeviceDisplay.Current.MainDisplayInfo;
        return display.Height / display.Density * 0.5;
    }
}
