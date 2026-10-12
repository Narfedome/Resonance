using Resonance.Features.Onboarding;
using Resonance.Services;

namespace Resonance
{
    public partial class App : Application
    {
        private readonly AppShell _shell;
        private readonly IServiceProvider _services;

        public App(AppShell shell, IServiceProvider services)
        {
            InitializeComponent();
            _shell    = shell;
            _services = services;
        }

        protected override Window CreateWindow(IActivationState? activationState)
        {
            ThemeService.Instance.Initialize();

            bool hasLaunched = Preferences.Default.Get("has_launched", false);
            var page = hasLaunched ? _shell : (Page)_services.GetRequiredService<OnboardingPage>();

            // L'UI est pensée pour un usage mobile (portrait), et rien dans la mise en page ne profite
            // d'une largeur au-delà de ce que montre la Bibliothèque : GridItemsLayout Span="3" avec des
            // tuiles WidthRequest=120 (LibraryTrackView.xaml) plafonne à 3 colonnes quelle que soit la
            // largeur dispo — une fenêtre plus large ne ferait qu'ajouter du vide autour. Le mixeur
            // (ChannelStripView, WidthRequest=50) défile horizontalement plutôt que d'avoir besoin de
            // place. Le vrai plancher, c'est la TabBar du Shell (AppShell.xaml) : 3 onglets (Campagnes/
            // Bibliothèque/Paramètres) tiennent dès ~480px, mais un 4ᵉ apparaît ("AudioMixer") dès
            // qu'une scène est active - testé, il faut ~590px pour que les 4 tiennent sans repli en
            // menu "...". 600 laisse une petite marge. Sur Mac Catalyst, sans bornes la fenêtre est
            // librement redimensionnable en format très large, ce qui casse cette mise en page pensée
            // pour du portrait — les bornes y restent donc actives. Windows en est délibérément exempté
            // (demande explicite) : Width/Height ne fixent plus qu'une taille de lancement, librement
            // redimensionnable ensuite dans les deux sens. Ignoré sur Android/iOS (le windowing n'y a
            // pas cours).
            var window = new Window(page)
            {
                // Nom affiché dans la barre de titre Windows, Alt+Tab et la barre des tâches : sans lui,
                // MAUI le laisse vide.
                Title = "Resonance : TTRPG Mixer",
                Width = 600,
                Height = 800,
#if !WINDOWS
                MinimumWidth = 600,
                MinimumHeight = 640,
                MaximumWidth = 680,
#endif
            };
#if WINDOWS
            // MAUI n'applique pas l'icône de l'appli à la fenêtre WinUI (barre des tâches, Alt+Tab) : on
            // lui donne l'appicon.ico que MauiIcon génère à côté de l'exe, aussi bien pour l'installeur
            // que pour le MSIX.
            window.HandlerChanged += (_, _) =>
            {
                var iconPath = Path.Combine(AppContext.BaseDirectory, "appicon.ico");
                if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow && File.Exists(iconPath))
                    nativeWindow.AppWindow.SetIcon(iconPath);
            };
#endif
            return window;
        }
    }
}
