using CommunityToolkit.Mvvm.Messaging;
using Resonance.Components;
using Resonance.Features.Campaigns;
using Resonance.Features.Library;
using Resonance.Models.ImportExport;
using Resonance.Models.Library;

namespace Resonance.Services
{
    /// <summary>
    /// Import d'un .dmpack côté appli : l'import Core (ImportExportService) plus tout ce qui dépend de
    /// l'UI ou de la localisation et doit suivre chaque import - réalignement des catégories par
    /// défaut sur la langue courante, préchauffe des pochettes, rafraîchissement des listes affichées.
    /// Partagé par l'écran Import/Export et la campagne d'exemple (SamplePackService), pour que les
    /// deux chemins restent identiques.
    /// </summary>
    public class PackImportService
    {
        // Clés des catégories par défaut (cf. MauiProgram) : ce sont des libellés localisés, pas des
        // clés stables, donc un import venant d'une install dans une autre langue (ex. Android EN vs
        // Windows FR) crée sinon des doublons ("Music" + "Musique") au lieu de fusionner sur la même
        // catégorie logique. Cf. ReconcileDefaultCategoryTranslationsAsync.
        private static readonly string[] DefaultCategoryKeys =
            { "LibCategoryMusic", "LibCategoryAmbience", "LibCategorySoundEffect" };

        private readonly IImportExportService _importExportService;
        private readonly ILibraryDataService _libraryDataService;
        private readonly CoverArtService _coverArtService;

        private static LocalizationService Loc => LocalizationService.Instance;

        public PackImportService(
            IImportExportService importExportService,
            ILibraryDataService libraryDataService,
            CoverArtService coverArtService)
        {
            _importExportService = importExportService;
            _libraryDataService = libraryDataService;
            _coverArtService = coverArtService;
        }

        /// <summary>
        /// Importe le pack en tenant <paramref name="progressViewModel"/> à jour. Retourne aussi la
        /// durée de préchauffe des pochettes, que <see cref="ImportResult.TotalDuration"/> ne couvre
        /// pas (calculée côté Core avant elle).
        /// </summary>
        public async Task<(ImportResult Result, TimeSpan CoverDuration)> ImportAsync(
            Stream source, ImportProgressViewModel progressViewModel, CancellationToken cancellationToken = default)
        {
            var progress = new Progress<ImportProgress>(p =>
            {
                // L'extraction peut atteindre Total avant que la vérification de décodabilité et
                // la sauvegarde en base n'aient fini (parallélisées séparément, cf.
                // ImportExportService.ImportTracksAsync) : sans ce message, la popup semblerait
                // bloquée à "Total/Total" pendant encore un moment.
                progressViewModel.CurrentFileName = p.IsVerifyingTracks ? Loc["ImportExportVerifyingTracks"] : p.CurrentItem;
                progressViewModel.TotalCount = p.Total;
                progressViewModel.ProcessedCount = p.Processed;
            });

            var result = await _importExportService.ImportAsync(source, progress, cancellationToken);
            await ReconcileDefaultCategoryTranslationsAsync();

            // Pré-chauffe les pochettes AVANT LibraryUpdatedMessage : sans ça, la Bibliothèque
            // recharge avec des ImagePath vides et chaque tuile relance sa propre extraction
            // TagLib au premier affichage, perceptible comme un ralentissement en scrollant juste
            // après un gros import - cf. discussion avec l'utilisateur.
            var coverDuration = await WarmUpImportedCoversAsync(result.ImportedTracks, progressViewModel);

            NotifyListsChanged();
            return (result, coverDuration);
        }

        /// <summary>
        /// Les campagnes/pistes/sorts importés sont insérés directement en base, sans passer par les
        /// commandes normales (Create/Edit) qui patchent déjà les listes affichées : sans ces messages,
        /// la page Campagnes et la Bibliothèque restent figées jusqu'au redémarrage de l'appli. Aussi
        /// appelé après un import annulé, qui a pu insérer une partie des données.
        /// </summary>
        public static void NotifyListsChanged()
        {
            WeakReferenceMessenger.Default.Send(new CampaignsUpdatedMessage());
            WeakReferenceMessenger.Default.Send(new LibraryUpdatedMessage());
        }

        /// <summary>
        /// Ramène, pour les catégories par défaut uniquement, toute variante connue dans une autre
        /// langue vers son libellé dans la langue actuelle de l'appli - les catégories créées par
        /// l'utilisateur ne sont jamais concernées, elles sont comparées telles quelles. Que la
        /// catégorie canonique existe déjà ici ou non (l'utilisateur a pu la renommer ou la
        /// supprimer), une catégorie par défaut réimportée doit malgré tout s'aligner sur la langue
        /// courante plutôt que de rester dans la langue de l'export : RenameCategoryAsync fusionne
        /// si la cible existe déjà, ou renomme simplement sinon.
        /// </summary>
        private async Task ReconcileDefaultCategoryTranslationsAsync()
        {
            foreach (var key in DefaultCategoryKeys)
            {
                var canonical = Loc[key];

                foreach (var languageCode in LocalizationService.SupportedLanguages.Keys)
                {
                    var variant = LocalizationService.GetString(key, languageCode);
                    if (string.Equals(variant, canonical, StringComparison.Ordinal))
                        continue;

                    await _libraryDataService.RenameCategoryAsync(typeof(Track), variant, canonical);
                    await _libraryDataService.RenameCategoryAsync(typeof(Spell), variant, canonical);
                }
            }
        }

        /// <summary>
        /// Pré-extrait et met en cache la pochette de chaque piste tout juste importée, au lieu de
        /// la laisser à l'extraction paresseuse au premier affichage dans la Bibliothèque - après un
        /// gros import, scroller la liste pour la première fois relançait sinon une extraction
        /// TagLib par piste au fil du défilement, perceptible comme un ralentissement. Affichée comme
        /// une phase supplémentaire de la popup d'import plutôt qu'en silence, pour que l'attente
        /// reste expliquée. GetCoverAsync limite déjà en interne à 3 extractions simultanées (cf.
        /// CoverArtService.CoverLoadThrottle) - aucune pistes réutilisées (dédupliquées par hash) n'y
        /// passent, uniquement celles tout juste copiées.
        /// </summary>
        private async Task<TimeSpan> WarmUpImportedCoversAsync(List<Track> tracks, ImportProgressViewModel progressViewModel)
        {
            if (tracks.Count == 0) return TimeSpan.Zero;

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            progressViewModel.CurrentFileName = Loc["ImportExportLoadingCovers"];
            progressViewModel.ProcessedCount = 0;
            progressViewModel.TotalCount = tracks.Count;

            var warmed = 0;
            await Task.WhenAll(tracks.Select(async track =>
            {
                try { await _coverArtService.GetCoverAsync(track); }
                catch { /* best effort : la Bibliothèque ré-extraira au premier affichage sinon */ }
                progressViewModel.ProcessedCount = Interlocked.Increment(ref warmed);
            }));

            return stopwatch.Elapsed;
        }
    }
}
