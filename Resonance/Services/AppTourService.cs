using Plugin.Maui.Onboarding;
using Plugin.Maui.Onboarding.Internals;
using Plugin.Maui.Onboarding.Views;

namespace Resonance.Services
{
    /// <summary>
    /// Visites guidées courtes (projecteur + bulle, cf. ThirdParty/PluginMauiOnboarding), une par écran,
    /// affichées la première fois qu'on y arrive plutôt qu'en un seul parcours au démarrage, pour ne pas
    /// submerger l'utilisateur. Chacune commence par une bulle centrée qui présente l'écran dans son
    /// ensemble, puis met ses contrôles en lumière un par un. Elles ne font qu'expliquer, sans demander de
    /// créer quoi que ce soit : une première tentative (branche archive/first-run-tutorial) qui faisait
    /// agir l'utilisateur à chaque étape s'était révélée trop fragile. Chaque page déclenche la sienne
    /// (cf. CampaignPage, LibraryTrackPage, ImportExportPage, ChannelSettingsDialog, CampaignViewModel.Launch
    /// pour le mixeur) ;
    /// une visite passée ou terminée ne revient plus, sauf via « Revoir » dans les Réglages.
    /// </summary>
    public class AppTourService
    {
        // Clés des contrôles mis en lumière, posées dans le XAML via OnboardingTargetBehavior. Quand une
        // clé est portée par plusieurs éléments (gabarit de ligne, vue réutilisée), le registre du plugin
        // retient le dernier affiché sur la page courante.
        public const string CampaignRowKey = "Tour.CampaignRow";
        public const string CampaignChevronKey = "Tour.CampaignChevron";
        public const string CampaignHandleKey = "Tour.CampaignHandle";
        public const string SessionRowKey = "Tour.SessionRow";
        public const string SceneRowKey = "Tour.SceneRow";
        public const string SceneLaunchKey = "Tour.SceneLaunch";
        public const string CampaignCreateKey = "Tour.CampaignCreate";
        public const string CampaignEditKey = "Tour.CampaignEdit";
        public const string CampaignDeleteKey = "Tour.CampaignDelete";

        public const string LibraryCategoryKey = "Tour.LibraryCategory";
        public const string LibraryManageCategoriesKey = "Tour.LibraryManageCategories";
        public const string LibraryTrackTileKey = "Tour.LibraryTrackTile";
        public const string LibraryTrackPlayKey = "Tour.LibraryTrackPlay";
        public const string LibraryTrackCheckKey = "Tour.LibraryTrackCheck";
        public const string LibrarySelectAllKey = "Tour.LibrarySelectAll";
        public const string LibraryAddKey = "Tour.LibraryAdd";
        public const string LibraryEditKey = "Tour.LibraryEdit";
        public const string LibraryDeleteKey = "Tour.LibraryDelete";
        public const string LibraryImportKey = "Tour.LibraryImport";

        public const string MixerSceneSelectorKey = "Tour.MixerSceneSelector";
        public const string MixerGlobalKey = "Tour.MixerGlobal";
        public const string MixerFreeformKey = "Tour.MixerFreeform";
        public const string MixerAddChannelKey = "Tour.MixerAddChannel";
        public const string MixerChannelKey = "Tour.MixerChannel";
        public const string MixerChannelBackgroundKey = "Tour.MixerChannelBackground";
        public const string MixerChannelPlayKey = "Tour.MixerChannelPlay";
        public const string MixerChannelStopKey = "Tour.MixerChannelStop";
        public const string MixerChannelVolumeKey = "Tour.MixerChannelVolume";
        public const string MixerChannelTrackKey = "Tour.MixerChannelTrack";
        public const string MixerChannelSettingsKey = "Tour.MixerChannelSettings";
        public const string MixerChannelRemoveKey = "Tour.MixerChannelRemove";
        public const string MixerChannelHandleKey = "Tour.MixerChannelHandle";
        public const string ChannelSettingsVolumeKey = "Tour.ChannelSettingsVolume";
        public const string ChannelSettingsLoopKey = "Tour.ChannelSettingsLoop";
        public const string ChannelSettingsFadeInKey = "Tour.ChannelSettingsFadeIn";
        public const string ChannelSettingsFadeOutKey = "Tour.ChannelSettingsFadeOut";
        public const string ChannelSettingsAutoPlayKey = "Tour.ChannelSettingsAutoPlay";

        public const string ImportExportExportKey = "Tour.ImportExportExport";
        public const string ImportExportLevelKey = "Tour.ImportExportLevel";
        public const string ImportExportCampaignKey = "Tour.ImportExportCampaign";
        public const string ImportExportImportKey = "Tour.ImportExportImport";

        private const string CampaignsTourKey = "resonance-campaigns";
        private const string LibraryTourKey = "resonance-library";
        private const string MixerTourKey = "resonance-mixer";
        private const string ImportExportTourKey = "resonance-import-export";
        private const string ChannelSettingsTourKey = "resonance-channel-settings";

        // Seules plateformes où le plugin sait lire la position d'un contrôle à l'écran (cf.
        // OnboardingBoundsExtensions) : ailleurs, chaque étape attendrait 10 s pour rien.
#if ANDROID || IOS || WINDOWS
        public static bool IsSupported => true;
#else
        public static bool IsSupported => false;
#endif

        private readonly OnboardingCoordinator _coordinator = new();
        private readonly ILibraryDataService _libraryDataService;

        public AppTourService(ILibraryDataService libraryDataService)
        {
            _libraryDataService = libraryDataService;
        }

        private static LocalizationService Loc => LocalizationService.Instance;

        public bool IsCampaignsTourPending => IsPending(CampaignsTourKey);
        public bool IsLibraryTourPending => IsPending(LibraryTourKey);
        public bool IsMixerTourPending => IsPending(MixerTourKey);
        public bool IsImportExportTourPending => IsPending(ImportExportTourKey);
        public bool IsChannelSettingsTourPending => IsPending(ChannelSettingsTourKey);

        /// <summary>Fait revenir toutes les visites, chacune à la prochaine arrivée sur son écran.</summary>
        public void ResetAll()
        {
            foreach (var key in new[] { CampaignsTourKey, LibraryTourKey, MixerTourKey, ImportExportTourKey, ChannelSettingsTourKey })
                OnboardingCompletionStore.SetCompleted(key, false);
        }

        /// <param name="hasScene">Faux si aucune scène n'a pu être dépliée : les étapes chapitre, scène
        /// et Lancer sont alors sautées (le plugin attendrait sinon 10 s une cible absente).</param>
        public Task StartCampaignsTourAsync(bool hasScene)
        {
            var builder = OnboardingTourBuilder.Create(CampaignsTourKey)
                .AddStep(Intro("TourCampaignIntroTitle", "TourCampaignIntroBody"))
                .AddStep(Step(CampaignRowKey, "TourCampaignTitle", "TourCampaignBody"))
                .AddStep(Step(CampaignChevronKey, "TourChevronTitle", "TourChevronBody"))
                .AddStep(Step(CampaignHandleKey, "TourCampaignHandleTitle", "TourCampaignHandleBody"));
            if (hasScene)
            {
                builder.AddStep(Step(SessionRowKey, "TourSessionTitle", "TourSessionBody"))
                    .AddStep(Step(SceneRowKey, "TourSceneTitle", "TourSceneBody"))
                    .AddStep(Step(SceneLaunchKey, "TourLaunchTitle", "TourLaunchBody"));
            }

            return StartAsync(builder
                .AddStep(Step(CampaignCreateKey, "TourCreateTitle", "TourCreateBody"))
                .AddStep(Step(CampaignEditKey, "TourEditTitle", "TourEditBody"))
                .AddStep(Step(CampaignDeleteKey, "TourDeleteTitle", "TourDeleteBody"))
                .Build());
        }

        /// <param name="hasTracks">Faux sur une Bibliothèque vide : les étapes sur la tuile d'une piste
        /// sont alors sautées (le plugin attendrait sinon 10 s une cible absente).</param>
        public Task StartLibraryTourAsync(bool hasTracks)
        {
            var builder = OnboardingTourBuilder.Create(LibraryTourKey)
                .AddStep(Intro("TourLibraryIntroTitle", "TourLibraryIntroBody"))
                .AddStep(Step(LibraryCategoryKey, "TourLibraryCategoryTitle", "TourLibraryCategoryBody"))
                .AddStep(Step(LibraryManageCategoriesKey, "TourLibraryManageTitle", "TourLibraryManageBody"));
            if (hasTracks)
            {
                builder.AddStep(Step(LibraryTrackTileKey, "TourLibraryTileTitle", "TourLibraryTileBody"))
                    .AddStep(Step(LibraryTrackPlayKey, "TourLibraryPlayTitle", "TourLibraryPlayBody"))
                    .AddStep(Step(LibraryTrackCheckKey, "TourLibraryCheckTitle", "TourLibraryCheckBody"));
            }

            return StartAsync(builder
                .AddStep(Step(LibrarySelectAllKey, "TourLibrarySelectAllTitle", "TourLibrarySelectAllBody"))
                .AddStep(Step(LibraryAddKey, "TourLibraryAddTitle", "TourLibraryAddBody"))
                .AddStep(Step(LibraryEditKey, "TourLibraryEditTitle", "TourLibraryEditBody"))
                .AddStep(Step(LibraryDeleteKey, "TourLibraryDeleteTitle", "TourLibraryDeleteBody"))
                .AddStep(Step(LibraryImportKey, "TourLibraryImportTitle", "TourLibraryImportBody"))
                .Build());
        }

        /// <summary>
        /// Visite du mixeur, adaptée au contenu de la scène lancée : avec des canaux, elle montre la
        /// tranche entière d'un canal, son fond, puis chacun de ses contrôles (le dialogue de réglages a
        /// sa propre visite, à sa première ouverture) ; sans canal, l'étape « Ajouter un canal » explique
        /// quoi faire selon que la Bibliothèque contient déjà des pistes ou non.
        /// </summary>
        public async Task StartMixerTourAsync(bool hasChannels)
        {
            var libraryHasTracks = hasChannels
                || (await _libraryDataService.GetItemIdsAsync(typeof(Models.Library.Track), null)).Count > 0;
            var addChannelBody = hasChannels ? "TourMixerAddBody"
                : libraryHasTracks ? "TourMixerAddEmptySceneBody"
                : "TourMixerAddNoTracksBody";

            var builder = OnboardingTourBuilder.Create(MixerTourKey)
                .AddStep(Intro("TourMixerIntroTitle", "TourMixerIntroBody"))
                .AddStep(Step(MixerSceneSelectorKey, "TourMixerSceneTitle", "TourMixerSceneBody"))
                .AddStep(Step(MixerGlobalKey, "TourMixerGlobalTitle", "TourMixerGlobalBody"))
                .AddStep(Step(MixerFreeformKey, "TourMixerFreeformTitle", "TourMixerFreeformBody"))
                .AddStep(Step(MixerAddChannelKey, "TourMixerAddTitle", addChannelBody));
            if (hasChannels)
            {
                builder.AddStep(Step(MixerChannelKey, "TourMixerChannelTitle", "TourMixerChannelBody"))
                    .AddStep(Step(MixerChannelBackgroundKey, "TourMixerChannelBackgroundTitle", "TourMixerChannelBackgroundBody"))
                    .AddStep(Step(MixerChannelPlayKey, "TourMixerChannelPlayTitle", "TourMixerChannelPlayBody"))
                    .AddStep(Step(MixerChannelStopKey, "TourMixerChannelStopTitle", "TourMixerChannelStopBody"))
                    .AddStep(Step(MixerChannelVolumeKey, "TourMixerChannelVolumeTitle", "TourMixerChannelVolumeBody"))
                    .AddStep(Step(MixerChannelTrackKey, "TourMixerChannelTrackTitle", "TourMixerChannelTrackBody"))
                    .AddStep(Step(MixerChannelSettingsKey, "TourMixerChannelSettingsTitle", "TourMixerChannelSettingsBody"))
                    .AddStep(Step(MixerChannelRemoveKey, "TourMixerChannelRemoveTitle", "TourMixerChannelRemoveBody"))
                    .AddStep(Step(MixerChannelHandleKey, "TourMixerChannelHandleTitle", "TourMixerChannelHandleBody"));
            }

            await StartAsync(builder.Build());
        }

        // Chaque bloc est montré en entier, avec un détour par les deux sélections de l'export. La carte
        // récapitulative du haut n'a pas d'étape : elle dit déjà, en permanence, ce que dirait sa bulle.
        public Task StartImportExportTourAsync() => StartAsync(OnboardingTourBuilder.Create(ImportExportTourKey)
            .AddStep(Intro("TourImportExportIntroTitle", "TourImportExportIntroBody"))
            .AddStep(Step(ImportExportExportKey, "TourExportTitle", "TourExportBody"))
            .AddStep(Step(ImportExportLevelKey, "TourExportLevelTitle", "TourExportLevelBody"))
            .AddStep(Step(ImportExportCampaignKey, "TourExportCampaignTitle", "TourExportCampaignBody"))
            .AddStep(Step(ImportExportImportKey, "TourImportTitle", "TourImportBody"))
            .Build());

        public Task StartChannelSettingsTourAsync() => StartAsync(OnboardingTourBuilder.Create(ChannelSettingsTourKey)
            .AddStep(Intro("TourSettingsIntroTitle", "TourSettingsIntroBody"))
            .AddStep(Step(ChannelSettingsVolumeKey, "TourSettingsVolumeTitle", "TourSettingsVolumeBody"))
            .AddStep(Step(ChannelSettingsLoopKey, "TourSettingsLoopTitle", "TourSettingsLoopBody"))
            .AddStep(Step(ChannelSettingsFadeInKey, "TourSettingsFadeInTitle", "TourSettingsFadeInBody"))
            .AddStep(Step(ChannelSettingsFadeOutKey, "TourSettingsFadeOutTitle", "TourSettingsFadeOutBody"))
            .AddStep(Step(ChannelSettingsAutoPlayKey, "TourSettingsAutoPlayTitle", "TourSettingsAutoPlayBody"))
            .Build());

        private static bool IsPending(string tourKey) => IsSupported && !OnboardingCompletionStore.IsCompleted(tourKey);

        private static OnboardingStep Step(string targetKey, string titleKey, string bodyKey) => new()
        {
            TargetKey = targetKey,
            Title = Loc[titleKey],
            Description = Loc[bodyKey]
        };

        // Bulle centrée, sans projecteur : présente l'écran avant d'en détailler les contrôles.
        private static OnboardingStep Intro(string titleKey, string bodyKey) => Step(string.Empty, titleKey, bodyKey);

        // Sans effet si une autre visite est déjà en cours : celle-ci reste en attente et reviendra à la
        // prochaine arrivée sur son écran.
        private async Task StartAsync(OnboardingTour tour)
        {
            if (!IsSupported) return;

            OnboardingTooltipCard.NextLabel = Loc["TourNext"];
            OnboardingTooltipCard.DoneLabel = Loc["TourDone"];
            OnboardingTooltipCard.SkipLabel = Loc["TourSkip"];
            ApplyThemeColors();

            await _coordinator.StartTourIfNotCompletedAsync(tour);
        }

        // Relu à chaque lancement : la palette a pu changer entre deux visites. La bulle et la lueur
        // prennent la surface et l'accent de la palette ; le voile, lui, reste noir quel que soit le thème.
        private void ApplyThemeColors()
        {
            var resources = Application.Current!.Resources;
            var accent = (Color)resources["AppAccent"];

            _coordinator.TooltipBackgroundColor = (Color)resources["AppSurface"];
            _coordinator.TooltipBorderColor = accent;
            _coordinator.GlowColor = accent.WithAlpha(0.8f);
            _coordinator.ScrimColor = Colors.Black.WithAlpha(0.7f);
        }
    }
}
