using Plugin.Maui.Onboarding;
using Plugin.Maui.Onboarding.Views;

namespace Resonance.Services
{
    /// <summary>
    /// Visite guidée courte (projecteur + bulle, cf. ThirdParty/PluginMauiOnboarding) qui ne fait
    /// qu'expliquer, sans demander de créer quoi que ce soit : une première tentative (branche
    /// archive/first-run-tutorial) qui faisait agir l'utilisateur à chaque étape s'était révélée trop
    /// fragile. Demandée à la fin de l'accueil quand la campagne d'exemple a été chargée, ou depuis les
    /// Réglages ; lancée par CampaignPage une fois la liste chargée et la campagne d'exemple dépliée.
    /// </summary>
    public class AppTourService
    {
        // Clés des contrôles mis en lumière, posées dans le XAML via OnboardingTargetBehavior. Le
        // registre du plugin est global à l'appli : une clé posée dans un gabarit de ligne (campagne,
        // Lancer) vise la dernière ligne de ce type affichée - la campagne d'exemple, tout juste
        // importée, est la dernière de la liste.
        public const string CampaignRowKey = "Tour.CampaignRow";
        public const string SceneLaunchKey = "Tour.SceneLaunch";
        public const string CampaignActionsKey = "Tour.CampaignActions";
        public const string LibraryActionsKey = "Tour.LibraryActions";

        private const string TourKey = "resonance-first-run";

        // Seules plateformes où le plugin sait lire la position d'un contrôle à l'écran (cf.
        // OnboardingBoundsExtensions) : ailleurs, chaque étape attendrait 10 s pour rien.
#if ANDROID || IOS || WINDOWS
        public static bool IsSupported => true;
#else
        public static bool IsSupported => false;
#endif

        private readonly OnboardingCoordinator _coordinator = new();
        private bool _isRequested;

        private static LocalizationService Loc => LocalizationService.Instance;

        /// <summary>Demande la visite au prochain affichage de la page Campagnes.</summary>
        public void Request()
        {
            if (IsSupported) _isRequested = true;
        }

        /// <summary>Vrai une seule fois par demande : CampaignPage l'interroge à chaque navigation.</summary>
        public bool ConsumeRequest()
        {
            var requested = _isRequested;
            _isRequested = false;
            return requested;
        }

        /// <param name="includeSceneStep">Faux si aucune scène n'a pu être dépliée : le plugin
        /// attendrait sinon 10 s une cible absente avant d'afficher quoi que ce soit.</param>
        public async Task StartAsync(bool includeSceneStep)
        {
            OnboardingTooltipCard.NextLabel = Loc["TourNext"];
            OnboardingTooltipCard.DoneLabel = Loc["TourDone"];
            OnboardingTooltipCard.SkipLabel = Loc["TourSkip"];
            ApplyThemeColors();

            // Replay plutôt que StartTourIfNotCompleted : c'est Resonance qui décide quand la proposer
            // (fin d'accueil, Réglages), pas l'état "déjà vue" mémorisé par le plugin.
            await _coordinator.ReplayTourAsync(BuildTour(includeSceneStep));
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

        private static OnboardingTour BuildTour(bool includeSceneStep)
        {
            var builder = OnboardingTourBuilder.Create(TourKey)
                .AddStep(new OnboardingStep
                {
                    TargetKey = CampaignRowKey,
                    Title = Loc["TourCampaignTitle"],
                    Description = Loc["TourCampaignBody"]
                });

            if (includeSceneStep)
            {
                builder.AddStep(new OnboardingStep
                {
                    TargetKey = SceneLaunchKey,
                    Title = Loc["TourLaunchTitle"],
                    Description = Loc["TourLaunchBody"]
                });
            }

            return builder
                .AddStep(new OnboardingStep
                {
                    TargetKey = CampaignActionsKey,
                    Title = Loc["TourActionsTitle"],
                    Description = Loc["TourActionsBody"]
                })
                .AddStep(new OnboardingStep
                {
                    TargetKey = LibraryActionsKey,
                    Title = Loc["TourLibraryTitle"],
                    Description = Loc["TourLibraryBody"],
                    RequiredRoute = "//LibraryTrackPage"
                })
                .Build();
        }
    }
}
