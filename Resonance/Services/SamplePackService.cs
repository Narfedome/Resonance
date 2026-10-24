using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using Resonance.Components;
using Resonance.Models.ImportExport;

namespace Resonance.Services
{
    /// <summary>
    /// Campagne d'exemple embarquée dans l'appli (Resources/Raw/SamplePack, générée par
    /// Deploy/SamplePack/Build-SamplePack.ps1) : un .dmpack par langue, mêmes pistes, titres traduits.
    /// Proposée à la fin de l'accueil et rechargeable depuis les Réglages.
    /// </summary>
    public class SamplePackService
    {
        private const string ImportedPreferenceKey = "sample_campaign_imported";
        private const string FallbackLanguage = "en";

        private readonly PackImportService _packImportService;

        private static LocalizationService Loc => LocalizationService.Instance;

        public SamplePackService(PackImportService packImportService)
        {
            _packImportService = packImportService;
        }

        /// <summary>
        /// Vrai si l'exemple a déjà été importé sur cette install : l'import crée toujours de
        /// nouvelles campagnes (seules les pistes sont dédupliquées par hash), un second import
        /// donnerait donc une seconde copie de la campagne.
        /// </summary>
        public bool HasImportedSample => Preferences.Default.Get(ImportedPreferenceKey, false);

        /// <summary>
        /// Importe le pack de la langue courante, avec la popup de progression affichée sur
        /// <paramref name="host"/> (passée explicitement : pendant l'accueil, la fenêtre n'affiche pas
        /// encore l'AppShell et Shell.Current vaut null). Les erreurs remontent à l'appelant.
        /// </summary>
        public async Task<ImportResult> ImportAsync(Page host)
        {
            var popupView = new ImportProgressPopupView();
            popupView.ViewModel.Title = Loc["SampleImportInProgress"];
            host.ShowPopup(popupView, new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = false });

            // Copie dans le cache avant lecture : sur Android, le flux d'un asset embarqué n'est pas
            // "seekable", et ZipArchive chargerait alors tout le pack en mémoire.
            var tempPath = Path.Combine(FileSystem.CacheDirectory, $"sample-{Guid.NewGuid():N}.dmpack");
            try
            {
                await using (var asset = await FileSystem.OpenAppPackageFileAsync(AssetName(Loc.Language)))
                await using (var copy = File.Create(tempPath))
                    await asset.CopyToAsync(copy);

                ImportResult result;
                await using (var stream = File.OpenRead(tempPath))
                    (result, _) = await _packImportService.ImportAsync(stream, popupView.ViewModel);

                Preferences.Default.Set(ImportedPreferenceKey, true);
                return result;
            }
            finally
            {
                await host.ClosePopupAsync();
                try { File.Delete(tempPath); } catch (IOException) { }
            }
        }

        // Un pack par langue de l'appli (cf. LocalizationService.SupportedLanguages) ; repli sur
        // l'anglais si une langue est ajoutée à l'appli avant son pack.
        private static string AssetName(string? language)
        {
            var lang = language is "fr" or "en" ? language : FallbackLanguage;
            return $"SamplePack/sample-campaign.{lang}.dmpack";
        }
    }
}
