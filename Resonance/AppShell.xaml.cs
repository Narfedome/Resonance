using CommunityToolkit.Mvvm.Messaging;
using Resonance.Extensions;
using Resonance.Features.Campaigns;
using Resonance.Features.ImportExport;
using Resonance.Features.Library;
using Resonance.Services;

namespace Resonance
{
    public partial class AppShell : Shell
    {
        private readonly LocalizationService _loc = LocalizationService.Instance;

        public AppShell()
        {
            InitializeComponent();
            Routing.RegisterRoute(nameof(LibrarySpellEditPage), typeof(LibrarySpellEditPage));
            Routing.RegisterRoute(nameof(CategoryListPage), typeof(CategoryListPage));
            Routing.RegisterRoute(nameof(ImportExportPage), typeof(ImportExportPage));

            WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this,
                (r, m) => ((AppShell)r).UpdateTabTitles());
            UpdateTabTitles();
        }

        private void UpdateTabTitles()
        {
            CampaignsTab.Title    = _loc["TabCampaigns"];
            AudioMixerTab.Title   = _loc["TabAudiomixer"];
            LibraryTab.Title      = _loc["TabLibrary"];
            TracksContent.Title   = _loc["TabTracks"];
            SpellsContent.Title   = _loc["TabSpells"];
            SettingsTab.Title     = _loc["TabSettings"];
        }
    }
}
