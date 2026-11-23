namespace Resonance.UITests;

/// <summary>Parcours complets sur l'appli Android, chacun depuis une installation neuve (voir AppSession).</summary>
public class MixerScenarios
{
    /// <summary>Campagne d'exemple chargée depuis les Paramètres, première scène lancée depuis les Campagnes : le
    /// mixeur affiche ses canaux, chacun avec le nom de sa piste.</summary>
    [E2EFact]
    public void LaunchASampleScene_ShowsItsChannelsInTheMixer()
    {
        using var app = new AppSession();
        try
        {
            LoadSampleCampaign(app);
            var channels = LaunchFirstScene(app);

            Assert.NotEmpty(channels);
            Assert.All(channels, name => Assert.False(string.IsNullOrWhiteSpace(name), "Canal sans nom de piste"));
        }
        catch
        {
            app.Screenshot(nameof(LaunchASampleScene_ShowsItsChannelsInTheMixer));
            throw;
        }
    }

    /// <summary>Scène d'exemple lancée, puis la piste de son premier canal supprimée depuis la Bibliothèque : de
    /// retour sur le mixeur, ce canal a disparu et les autres sont toujours là.</summary>
    [E2EFact]
    public void DeleteATrackFromTheLibrary_RemovesItsChannelFromTheMixer()
    {
        using var app = new AppSession();
        try
        {
            LoadSampleCampaign(app);
            var before = LaunchFirstScene(app);
            var deleted = before[0];

            app.TapTab("Bibliothèque", "Library");
            app.ScrollToText(deleted).Click(); // sélectionne la tuile
            AppSession.Pause();
            app.Tap("LibraryDeleteButton");
            app.Confirm(); // « Supprimer la piste ... ? » → Oui
            AppSession.Pause(1500);
            Assert.False(app.HasText(deleted), $"La piste « {deleted} » est toujours dans la Bibliothèque");

            app.TapTab("Mixeur", "Mixer");
            var after = ChannelNames(app);
            Assert.DoesNotContain(deleted, after);
            Assert.Equal(before.Count - before.Count(n => n == deleted), after.Count);
        }
        catch
        {
            app.Screenshot(nameof(DeleteATrackFromTheLibrary_RemovesItsChannelFromTheMixer));
            throw;
        }
    }

    /// <summary>Paramètres › Découvrir Resonance › Charger, puis OK sur le message de fin d'import.</summary>
    private static void LoadSampleCampaign(AppSession app)
    {
        app.TapTab("Paramètres", "Settings");
        app.ScrollTo("SettingsSampleButton").Click();
        app.Confirm(seconds: 90); // l'import du pack peut prendre un moment sur l'émulateur
    }

    /// <summary>Campagnes : ouvre la première campagne et son premier chapitre, lance la première scène et rend
    /// les noms des canaux affichés par le mixeur une fois chargés.</summary>
    private static List<string> LaunchFirstScene(AppSession app)
    {
        app.TapTab("Campagnes", "Campaigns");
        app.Tap("CampaignRow");
        AppSession.Pause();
        app.Tap("SessionRow");
        AppSession.Pause();
        app.Tap("SceneLaunchButton");

        app.WaitUntil(() => app.Exists("MixerChannel"), "Aucun canal dans le mixeur après le lancement de la scène");
        AppSession.Pause(2000); // fin du chargement des lecteurs
        return ChannelNames(app);
    }

    private static List<string> ChannelNames(AppSession app) =>
        app.All("MixerChannelTrackName").Select(AppSession.TextOf).ToList();
}
