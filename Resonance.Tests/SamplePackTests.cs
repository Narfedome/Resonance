using Resonance.Models.Library;

namespace Resonance.Tests;

/// <summary>
/// Vérifie les packs de la campagne d'exemple réellement embarqués dans l'appli
/// (Resonance/Resources/Raw/SamplePack, générés par Deploy/SamplePack/Build-SamplePack.ps1) : un pack
/// mal signé ou une piste non décodable ne se verrait sinon qu'au premier lancement.
/// </summary>
public class SamplePackTests
{
    private static string PackPath(string language)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Resonance", "Resonance.csproj")))
            dir = dir.Parent;

        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "Resonance", "Resources", "Raw", "SamplePack", $"sample-campaign.{language}.dmpack");
    }

    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    public async Task Import_SamplePack_CreatesOneCampaignWithPlayableScenes(string language)
    {
        await using var ctx = await ImportExportTestContext.CreateAsync();

        await using (var stream = File.OpenRead(PackPath(language)))
        {
            var result = await ctx.Service.ImportAsync(stream);

            Assert.Equal(1, result.CampaignsImported);
            Assert.Equal(0, result.TracksRejected);
            Assert.True(result.TracksCopied > 0);
        }

        var campaign = (await ctx.Scenes.GetCampaignsAsync()).Single();
        foreach (var session in await ctx.Scenes.GetSessionsAsync(campaign.Id))
        {
            var scenes = await ctx.Scenes.GetScenesAsync(session.Id);
            Assert.NotEmpty(scenes);
            foreach (var scene in scenes)
                Assert.NotEmpty(await ctx.Scenes.GetSceneTracksAsync(scene.Id));
        }
    }

    [Fact]
    public async Task Import_BothSamplePacks_SharesTracks_ButDuplicatesCampaign()
    {
        await using var ctx = await ImportExportTestContext.CreateAsync();

        await using (var stream = File.OpenRead(PackPath("fr")))
            await ctx.Service.ImportAsync(stream);
        var trackCount = (await ctx.Library.GetAllItemsTypeAsync(typeof(Track))).Count;

        await using (var stream = File.OpenRead(PackPath("en")))
        {
            var result = await ctx.Service.ImportAsync(stream);
            Assert.Equal(0, result.TracksCopied);
            Assert.Equal(trackCount, result.TracksReused);
        }

        // Les pistes sont dédupliquées par hash, pas les campagnes : d'où la confirmation avant un
        // second chargement de l'exemple (cf. SamplePackService.HasImportedSample).
        Assert.Equal(2, (await ctx.Scenes.GetCampaignsAsync()).Count);
        Assert.Equal(trackCount, (await ctx.Library.GetAllItemsTypeAsync(typeof(Track))).Count);
    }
}
