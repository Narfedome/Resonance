using Microsoft.Maui.Handlers;

namespace Resonance.Platforms.Android;

/// <summary>
/// Personnalisations de handlers natifs Android (pendant de Platforms/Windows/WindowsHandlers).
/// </summary>
public static class AndroidHandlers
{
    public static void Configure(IMauiHandlersCollection handlers)
    {
        // Le SearchView natif dessine un trait sous le champ (search_plate) : la recherche de la
        // Bibliothèque est désormais un champ arrondi sur fond sombre (cf. LibraryTrackView), le
        // trait y ferait double emploi. On rend le fond de la plaque transparent.
        SearchBarHandler.Mapper.AppendToMapping("NoUnderline", (handler, view) =>
        {
            // SearchView d'AndroidX (identifiant dans le paquet de l'appli) ou du framework ("android").
            var context = handler.PlatformView.Context;
            foreach (var package in new[] { context?.PackageName, "android" })
            {
                var plateId = context?.Resources?.GetIdentifier("search_plate", "id", package) ?? 0;
                if (plateId != 0 && handler.PlatformView.FindViewById(plateId) is global::Android.Views.View plate)
                {
                    plate.Background = new global::Android.Graphics.Drawables.ColorDrawable(global::Android.Graphics.Color.Transparent);
                    return;
                }
            }
        });
    }
}
