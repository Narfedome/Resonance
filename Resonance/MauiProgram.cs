using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Core;
using Resonance.Components;
using Resonance.Data;
using Resonance.Features.AudioMixer;
using Resonance.Features.Campaigns;
using Resonance.Features.ImportExport;
using Resonance.Features.Library;
using Resonance.Models.Library;
using Resonance.Models;
using Resonance.Features.Onboarding;
using Resonance.Features.Settings;
using Resonance.Services;
using Microsoft.Extensions.Logging;
using Plugin.Maui.Audio;

namespace Resonance
{
    public static class MauiProgram
    {
        static readonly string dbPath = Path.Combine(FileSystem.AppDataDirectory, "resonance.db3");

        public static MauiApp CreateMauiApp()
        {
            string tracksDir = Path.Combine(FileSystem.AppDataDirectory, "Tracks");
            Directory.CreateDirectory(tracksDir);
            var builder = MauiApp.CreateBuilder();
            builder
                .UseMauiApp<App>()
                .UseMauiCommunityToolkit(options =>
                {
                    // Toutes les popups de l'app (dialogues thémés, import) dessinent leur propre
                    // carte (Border) : on désactive le cadre/l'ombre par défaut du toolkit pour éviter
                    // un double contour (bug connu de bordure blanche sur Windows).
                    options.SetPopupOptionsDefaults(new DefaultPopupOptionsSettings
                    {
                        Shape = null,
                        Shadow = null
                    });
                })
                .UseMauiCommunityToolkitCore()
                .ConfigureFonts(fonts =>
                {
                    fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                    fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
                    fonts.AddFont("PirataOne-Regular.ttf", "PirataOne");
                    fonts.AddFont("Font Awesome 7 Free-Regular-400.otf", "FontRegular");
                    fonts.AddFont("Font Awesome 7 Brands-Regular-400.otf", "FontBrands");
                    fonts.AddFont("Font Awesome 7 Free-Solid-900.otf", "FontSolid");
                    fonts.AddFont("rpgawesome-webfont.ttf", "RpgAwesome");
                })
#if WINDOWS
                // Cf. Resonance.Platforms.Windows.WindowsHandlers : personnalisations de handlers
                // natives WinUI, isolées dans Platforms/Windows pour ne pas polluer ce fichier.
                .ConfigureMauiHandlers(Platforms.Windows.WindowsHandlers.Configure)
#endif
                ;
            builder.AddAudio();

            // Le dispatcher UI du ChannelStripViewModel (projet Core, sans dépendance MAUI) est
            // branché ici sur le vrai thread UI.
            ChannelStripViewModel.UiDispatcher = action => MainThread.BeginInvokeOnMainThread(action);

            // Les catégories par défaut sont localisées ici : la couche données (Core) ne dépend
            // pas de la localisation.
            builder.Services.AddSingleton(
                new AppDatabase(dbPath, new[]
                {
                    LocalizationService.Instance["LibCategoryMusic"],
                    LocalizationService.Instance["LibCategoryAmbience"],
                    LocalizationService.Instance["LibCategorySoundEffect"]
                }));
            builder.Services.AddTransient<LoadingService>();
            builder.Services.AddSingleton<AudioPlayerService>();
            builder.Services.AddSingleton<AudioMixerService>();
            builder.Services.AddSingleton<FileService>();
            builder.Services.AddSingleton<ITrackFileStore>(sp => sp.GetRequiredService<FileService>());
            builder.Services.AddSingleton<ILibraryPickerService, LibraryPickerService>();
            builder.Services.AddSingleton<ILibraryPickerNavigationService, LibraryPickerNavigationService>();
            builder.Services.AddSingleton<ILibraryDataService, LibraryDataService>();
            builder.Services.AddSingleton<CoverArtService>();
            builder.Services.AddSingleton<IStorageService, StorageService>();
            builder.Services.AddSingleton<ISceneDataService, SceneDataService>();
            builder.Services.AddSingleton<IImportExportService, ImportExportService>();
            builder.Services.AddTransient<OnboardingViewModel>();
            builder.Services.AddTransient<OnboardingPage>();
            builder.Services.AddTransient<SettingsViewModel>();
            builder.Services.AddTransient<SettingsPage>();
            builder.Services.AddTransient<LibraryTrackViewModel>();
            builder.Services.AddTransient<CategoryListViewModel>();
            builder.Services.AddTransient<CategoryListPage>();
            builder.Services.AddTransient<LibrarySpellViewModel>();
            builder.Services.AddTransient<LibrarySpellEditViewModel>();
            builder.Services.AddTransient<LibrarySpellSelectorPage>();
            builder.Services.AddTransient<LibraryTrackSelectorPage>();
            builder.Services.AddSingleton<AudioMixerViewModel>();
            builder.Services.AddTransient<AudioMixerPage>();
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddTransient<CampaignViewModel>();
            builder.Services.AddTransient<CampaignPage>();
            builder.Services.AddTransient<ImportExportViewModel>();
            builder.Services.AddTransient<ImportExportPage>();

#if DEBUG
            builder.Logging.AddDebug();
#endif

            return builder.Build();
        }
    }

}
