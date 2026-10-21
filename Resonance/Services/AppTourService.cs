using Plugin.Maui.Onboarding;
using Plugin.Maui.Onboarding.Internals;
using Plugin.Maui.Onboarding.Views;

namespace Resonance.Services
{
    /// <summary>
    /// Visites guidées courtes (projecteur + bulle, cf. ThirdParty/PluginMauiOnboarding), une par écran,
    /// affichées la première fois qu'on y arrive plutôt qu'en un seul parcours au démarrage, pour ne pas
    /// submerger l'utilisateur. Elles ne font qu'expliquer, sans demander de créer quoi que ce soit : une
    /// première tentative (branche archive/first-run-tutorial) qui faisait agir l'utilisateur à chaque
    /// étape s'était révélée trop fragile. Chaque page déclenche la sienne (cf. CampaignPage,
    /// LibraryTrackPage, CampaignViewModel.Launch pour le mixeur) ; une visite passée ou terminée ne
    /// revient plus, sauf via « Revoir » dans les Réglages.
    /// </summary>
    public class AppTourService
    {
        // Clés des contrôles mis en lumière, posées dans le XAML via OnboardingTargetBehavior. Quand une
        // clé est portée par plusieurs éléments (gabarit de ligne, vue réutilisée), le registre du plugin
        // retient le dernier affiché sur la page courante.
        public const string CampaignRowKey = "Tour.CampaignRow";
        public const string SceneLaunchKey = "Tour.SceneLaunch";
        public const string CampaignActionsKey = "Tour.CampaignActions";
        public const string LibraryCategoryKey = "Tour.LibraryCategory";
        public const string LibraryManageCategoriesKey = "Tour.LibraryManageCategories";
        public const string LibraryActionsKey = "Tour.LibraryActions";
        public const string MixerSceneSelectorKey = "Tour.MixerSceneSelector";
        public const string MixerGlobalKey = "Tour.MixerGlobal";
        public const string MixerFreeformKey = "Tour.MixerFreeform";
        public const string MixerAddChannelKey = "Tour.MixerAddChannel";
        public const string MixerChannelKey = "Tour.MixerChannel";
        public const string MixerChannelActionsKey = "Tour.MixerChannelActions";

        private const string CampaignsTourKey = "resonance-campaigns";
        private const string LibraryTourKey = "resonance-library";
        private const string MixerTourKey = "resonance-mixer";

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

        /// <summary>Fait revenir toutes les visites, chacune à la prochaine arrivée sur son écran.</summary>
        public void ResetAll()
        {
            foreach (var key in new[] { CampaignsTourKey, LibraryTourKey, MixerTourKey })
                OnboardingCompletionStore.SetCompleted(key, false);
        }

        /// <param name="includeSceneStep">Faux si aucune scène n'a pu être dépliée : le plugin
        /// attendrait sinon 10 s une cible absente avant d'afficher quoi que ce soit.</param>
        public Task StartCampaignsTourAsync(bool includeSceneStep)
        {
            var builder = OnboardingTourBuilder.Create(CampaignsTourKey)
                .AddStep(Step(CampaignRowKey, "TourCampaignTitle", "TourCampaignBody"));
            if (includeSceneStep)
                builder.AddStep(Step(SceneLaunchKey, "TourLaunchTitle", "TourLaunchBody"));
            builder.AddStep(Step(CampaignActionsKey, "TourActionsTitle", "TourActionsBody"));

            return StartAsync(builder.Build());
        }

        public Task StartLibraryTourAsync() => StartAsync(OnboardingTourBuilder.Create(LibraryTourKey)
            .AddStep(Step(LibraryCategoryKey, "TourLibraryCategoryTitle", "TourLibraryCategoryBody"))
            .AddStep(Step(LibraryManageCategoriesKey, "TourLibraryManageTitle", "TourLibraryManageBody"))
            .AddStep(Step(LibraryActionsKey, "TourLibraryTitle", "TourLibraryBody"))
            .Build());

        /// <summary>
        /// Visite du mixeur, adaptée au contenu de la scène lancée : avec des canaux, elle détaille la
        /// carte d'un canal ; sans canal, l'étape « Ajouter un canal » explique quoi faire selon que la
        /// Bibliothèque contient déjà des pistes ou non.
        /// </summary>
        public async Task StartMixerTourAsync(bool hasChannels)
        {
            var libraryHasTracks = hasChannels
                || (await _libraryDataService.GetItemIdsAsync(typeof(Models.Library.Track), null)).Count > 0;
            var addChannelBody = hasChannels ? "TourMixerAddBody"
                : libraryHasTracks ? "TourMixerAddEmptySceneBody"
                : "TourMixerAddNoTracksBody";

            var builder = OnboardingTourBuilder.Create(MixerTourKey)
                .AddStep(Step(MixerSceneSelectorKey, "TourMixerSceneTitle", "TourMixerSceneBody"))
                .AddStep(Step(MixerGlobalKey, "TourMixerGlobalTitle", "TourMixerGlobalBody"))
                .AddStep(Step(MixerFreeformKey, "TourMixerFreeformTitle", "TourMixerFreeformBody"))
                .AddStep(Step(MixerAddChannelKey, "TourMixerAddTitle", addChannelBody));
            if (hasChannels)
            {
                builder.AddStep(Step(MixerChannelKey, "TourMixerChannelTitle", "TourMixerChannelBody"));
                builder.AddStep(Step(MixerChannelActionsKey, "TourMixerChannelActionsTitle", "TourMixerChannelActionsBody"));
            }

            await StartAsync(builder.Build());
        }

        private static bool IsPending(string tourKey) => IsSupported && !OnboardingCompletionStore.IsCompleted(tourKey);

        private static OnboardingStep Step(string targetKey, string titleKey, string bodyKey) => new()
        {
            TargetKey = targetKey,
            Title = Loc[titleKey],
            Description = Loc[bodyKey]
        };

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
