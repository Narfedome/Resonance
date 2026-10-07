using Resonance.Data;
using Resonance.Data.Entities;
using Resonance.Models;
using Resonance.Models.Library;
using Resonance.Services;

namespace Resonance.Tests;

/// <summary>
/// Base commune : une vraie base SQLite dans un fichier temporaire unique par test (":memory:" est
/// exclu car SQLiteAsyncConnection partage la connexion sous-jacente par chaîne de connexion, ce
/// qui ferait fuiter l'état d'un test à l'autre).
/// </summary>
public abstract class DatabaseTestBase : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"resonance-tests-{Guid.NewGuid():N}.db3");

    protected AppDatabase Db { get; private set; } = null!;

    protected virtual IEnumerable<string> DefaultTrackCategories => Array.Empty<string>();

    public async Task InitializeAsync()
    {
        Db = new AppDatabase(_dbPath, DefaultTrackCategories);
        await Db.Initialization;
    }

    public async Task DisposeAsync()
    {
        await Db.Connection.CloseAsync();
        File.Delete(_dbPath);
    }
}

public class SceneDataServiceTests : DatabaseTestBase
{
    private SceneDataService Service => new(Db);

    [Fact]
    public async Task SaveCampaign_AssignsId_AndIsReadableBack()
    {
        var campaign = new Campaign { Title = "Curse of Strahd" };

        await Service.SaveCampaignAsync(campaign);

        Assert.True(campaign.Id > 0);
        var all = await Service.GetCampaignsAsync();
        Assert.Single(all);
        Assert.Equal("Curse of Strahd", all[0].Title);
    }

    [Fact]
    public async Task SaveCampaign_WithExistingId_Updates()
    {
        var campaign = new Campaign { Title = "Avant" };
        await Service.SaveCampaignAsync(campaign);

        campaign.Title = "Après";
        await Service.SaveCampaignAsync(campaign);

        var all = await Service.GetCampaignsAsync();
        Assert.Single(all);
        Assert.Equal("Après", all[0].Title);
    }

    [Fact]
    public async Task GetSessions_FiltersByCampaign()
    {
        await Service.SaveSessionAsync(new Session { CampaignId = 1, Title = "C1-S1" });
        await Service.SaveSessionAsync(new Session { CampaignId = 1, Title = "C1-S2" });
        await Service.SaveSessionAsync(new Session { CampaignId = 2, Title = "C2-S1" });

        var sessions = await Service.GetSessionsAsync(1);

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(1, s.CampaignId));
    }

    [Fact]
    public async Task SceneTrack_RoundTrip_PreservesAllSettings_AndOrdersByPosition()
    {
        var library = new LibraryDataService(Db);
        var track = new Track { Title = "Rain", FilePath = @"C:\rain.mp3" };
        await library.SaveLibraryItemAsync(track);

        await Service.SaveSceneTrackAsync(new SceneTrack
        {
            SceneId = 1, Track = track, Position = 1, Volume = 0.3,
            IsLooping = false, AutoPlay = true, FadeIn = true, FadeOut = true
        });
        await Service.SaveSceneTrackAsync(new SceneTrack
        {
            SceneId = 1, Track = track, Position = 0, Volume = 0.9
        });

        var loaded = await Service.GetSceneTracksAsync(1);

        Assert.Equal(2, loaded.Count);
        Assert.Equal(new[] { 0, 1 }, loaded.Select(st => st.Position).ToArray());

        var st1 = loaded[1];
        Assert.Equal(0.3, st1.Volume);
        Assert.False(st1.IsLooping);
        Assert.True(st1.AutoPlay);
        Assert.True(st1.FadeIn);
        Assert.True(st1.FadeOut);
        Assert.Equal("Rain", st1.Track.Title);

        // Le volume de la piste jointe reflète celui du strip, pas le volume par défaut de la track.
        Assert.Equal(0.3, st1.Track.Volume);
    }

    [Fact]
    public async Task GetSceneTracks_SkipsTracksDeletedFromLibrary()
    {
        var library = new LibraryDataService(Db);
        var track = new Track { Title = "Ghost", FilePath = @"C:\ghost.mp3" };
        await library.SaveLibraryItemAsync(track);
        await Service.SaveSceneTrackAsync(new SceneTrack { SceneId = 1, Track = track });

        await library.DeleteLibraryItem(track);

        Assert.Empty(await Service.GetSceneTracksAsync(1));
    }

    [Fact]
    public async Task UpdateSceneTrack_PersistsEverySetting()
    {
        var library = new LibraryDataService(Db);
        var track = new Track { Title = "Wind" };
        await library.SaveLibraryItemAsync(track);

        var sceneTrack = new SceneTrack { SceneId = 1, Track = track, Volume = 1.0 };
        await Service.SaveSceneTrackAsync(sceneTrack);

        await Service.UpdateSceneTrackAsync(sceneTrack.Id, volume: 0.5, isLooping: false, autoPlay: true, fadeIn: true, fadeOut: true);

        var loaded = (await Service.GetSceneTracksAsync(1)).Single();
        Assert.Equal(0.5, loaded.Volume);
        Assert.False(loaded.IsLooping);
        Assert.True(loaded.AutoPlay);
        Assert.True(loaded.FadeIn);
        Assert.True(loaded.FadeOut);
    }

    [Fact]
    public async Task UpdateSceneTrackSettings_PersistsSettings_ButPreservesAutoPlay()
    {
        var library = new LibraryDataService(Db);
        var track = new Track { Title = "Wind" };
        await library.SaveLibraryItemAsync(track);

        var sceneTrack = new SceneTrack { SceneId = 1, Track = track, Volume = 1.0, AutoPlay = true };
        await Service.SaveSceneTrackAsync(sceneTrack);

        // La sauvegarde automatique du mixer ne doit jamais écraser l'AutoPlay, qui est un
        // réglage explicite de l'utilisateur.
        await Service.UpdateSceneTrackSettingsAsync(sceneTrack.Id, volume: 0.4, isLooping: false, fadeIn: true, fadeOut: true);

        var loaded = (await Service.GetSceneTracksAsync(1)).Single();
        Assert.Equal(0.4, loaded.Volume);
        Assert.False(loaded.IsLooping);
        Assert.True(loaded.FadeIn);
        Assert.True(loaded.FadeOut);
        Assert.True(loaded.AutoPlay);
    }

    [Fact]
    public async Task DeleteScene_CascadesToSceneTracks()
    {
        var library = new LibraryDataService(Db);
        var track = new Track { Title = "Storm" };
        await library.SaveLibraryItemAsync(track);

        var scene = new Scene { SessionId = 1, Title = "Bataille" };
        await Service.SaveSceneAsync(scene);
        await Service.SaveSceneTrackAsync(new SceneTrack { SceneId = scene.Id, Track = track });

        await Service.DeleteSceneAsync(scene);

        Assert.Equal(0, await Db.Connection.Table<SceneTrackEntity>().CountAsync());
    }

    [Fact]
    public async Task DeleteSession_CascadesToScenesAndSceneTracks()
    {
        var library = new LibraryDataService(Db);
        var track = new Track { Title = "Storm" };
        await library.SaveLibraryItemAsync(track);

        var session = new Session { CampaignId = 1, Title = "Chapitre" };
        await Service.SaveSessionAsync(session);
        var scene = new Scene { SessionId = session.Id, Title = "Bataille" };
        await Service.SaveSceneAsync(scene);
        await Service.SaveSceneTrackAsync(new SceneTrack { SceneId = scene.Id, Track = track });

        await Service.DeleteSessionAsync(session);

        Assert.Equal(0, await Db.Connection.Table<SceneEntity>().CountAsync());
        Assert.Equal(0, await Db.Connection.Table<SceneTrackEntity>().CountAsync());
    }

    [Fact]
    public async Task DeleteCampaign_CascadesToSessionsScenesAndSceneTracks_WithoutTouchingOthers()
    {
        var library = new LibraryDataService(Db);
        var track = new Track { Title = "Storm" };
        await library.SaveLibraryItemAsync(track);

        var campaign = new Campaign { Title = "Condamnée" };
        await Service.SaveCampaignAsync(campaign);
        var session = new Session { CampaignId = campaign.Id, Title = "Chapitre" };
        await Service.SaveSessionAsync(session);
        var scene = new Scene { SessionId = session.Id, Title = "Bataille" };
        await Service.SaveSceneAsync(scene);
        await Service.SaveSceneTrackAsync(new SceneTrack { SceneId = scene.Id, Track = track });

        // Une seconde campagne complète qui doit rester intacte.
        var other = new Campaign { Title = "Épargnée" };
        await Service.SaveCampaignAsync(other);
        var otherSession = new Session { CampaignId = other.Id, Title = "Autre chapitre" };
        await Service.SaveSessionAsync(otherSession);
        var otherScene = new Scene { SessionId = otherSession.Id, Title = "Autre scène" };
        await Service.SaveSceneAsync(otherScene);
        await Service.SaveSceneTrackAsync(new SceneTrack { SceneId = otherScene.Id, Track = track });

        await Service.DeleteCampaignAsync(campaign);

        Assert.Empty(await Service.GetSessionsAsync(campaign.Id));
        Assert.Equal(0, await Db.Connection.Table<SceneEntity>().Where(s => s.SessionId == session.Id).CountAsync());
        Assert.Equal(0, await Db.Connection.Table<SceneTrackEntity>().Where(st => st.SceneId == scene.Id).CountAsync());

        Assert.Single(await Service.GetSessionsAsync(other.Id));
        Assert.Single(await Service.GetScenesAsync(otherSession.Id));
        Assert.Single(await Service.GetSceneTracksAsync(otherScene.Id));
    }
}

public class AppDatabaseMigrationTests
{
    [Fact]
    public async Task Initialization_PurgesOrphansLeftByOldNonCascadingDeletes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"resonance-tests-{Guid.NewGuid():N}.db3");
        var db = new AppDatabase(path);
        try
        {
            await db.Initialization;

            // Simule l'état laissé par les anciennes suppressions sans cascade : enfants sans parent.
            await db.Connection.InsertAsync(new SessionEntity { CampaignId = 999, Title = "Orphelin" });
            await db.Connection.InsertAsync(new SceneEntity { SessionId = 999, Title = "Orpheline" });
            await db.Connection.InsertAsync(new SceneTrackEntity { SceneId = 999, TrackId = 999 });

            // Relance l'initialisation (nouveau démarrage de l'appli sur la même base).
            var reopened = new AppDatabase(path);
            await reopened.Initialization;

            Assert.Equal(0, await reopened.Connection.Table<SessionEntity>().CountAsync());
            Assert.Equal(0, await reopened.Connection.Table<SceneEntity>().CountAsync());
            Assert.Equal(0, await reopened.Connection.Table<SceneTrackEntity>().CountAsync());
        }
        finally
        {
            await db.Connection.CloseAsync();
            File.Delete(path);
        }
    }
}

public class LibraryDataServiceTests : DatabaseTestBase
{
    private LibraryDataService Service => new(Db);

    private static Track MakeTrack(string title, string category = "", string hash = "") => new()
    {
        Title = title,
        FilePath = $@"C:\tracks\{title}.mp3",
        Category = category,
        Hash = hash
    };

    [Fact]
    public async Task SaveTrack_AssignsId_UpdateKeepsSingleRow()
    {
        var track = MakeTrack("Tavern");
        await Service.SaveLibraryItemAsync(track);
        Assert.True(track.Id > 0);

        track.Title = "Tavern v2";
        await Service.SaveLibraryItemAsync(track);

        var all = await Service.GetAllItemsTypeAsync(typeof(Track));
        Assert.Single(all);
        Assert.Equal("Tavern v2", all[0].Title);
    }

    [Fact]
    public async Task GetItemsPage_PaginatesAndFiltersByCategory()
    {
        for (int i = 1; i <= 5; i++)
            await Service.SaveLibraryItemAsync(MakeTrack($"Music{i}", category: "Musique"));
        await Service.SaveLibraryItemAsync(MakeTrack("Ambient", category: "Ambiance"));

        var page = await Service.GetItemsPageAsync(typeof(Track), skip: 2, take: 2, category: "Musique");

        Assert.Equal(2, page.Count);
        Assert.Equal(new[] { "Music3", "Music4" }, page.Select(t => t.Title).ToArray());
    }

    [Fact]
    public async Task FindTrackByHash_FindsDuplicate_ButExcludesSelf()
    {
        var track = MakeTrack("Original", hash: "AAA");
        await Service.SaveLibraryItemAsync(track);

        Assert.NotNull(await Service.FindTrackByHashAsync("AAA", excludeId: 0));
        Assert.Null(await Service.FindTrackByHashAsync("AAA", excludeId: track.Id));
        Assert.Null(await Service.FindTrackByHashAsync("BBB", excludeId: 0));
    }

    [Fact]
    public async Task UpdateTrackImagePath_OnlyTouchesImagePath_EvenIfInMemoryModelDiverged()
    {
        var track = MakeTrack("Tavern", category: "Musique", hash: "AAA");
        await Service.SaveLibraryItemAsync(track);

        // Simule une édition concurrente depuis une autre instance : la sauvegarde différée de la
        // pochette ne doit pas réintroduire l'ancien titre (c'est tout l'intérêt de l'UPDATE ciblé
        // par rapport à SaveLibraryItemAsync, qui réécrit la ligne entière).
        var edited = MakeTrack("Tavern renamed", category: "Ambiance", hash: "AAA");
        edited.Id = track.Id;
        await Service.SaveLibraryItemAsync(edited);

        await Service.UpdateTrackImagePathAsync(track.Id, @"C:\covers\tavern.jpg");

        var reloaded = (Track)(await Service.GetAllItemsTypeAsync(typeof(Track))).Single();
        Assert.Equal(@"C:\covers\tavern.jpg", reloaded.ImagePath);
        Assert.Equal("Tavern renamed", reloaded.Title);
        Assert.Equal("Ambiance", reloaded.Category);
    }

    [Fact]
    public async Task UpdateTrackImagePath_WithUnknownId_IsANoOp()
    {
        var track = MakeTrack("Tavern");
        await Service.SaveLibraryItemAsync(track);

        // Id 0 (modèle jamais persisté) ou inconnu : aucune ligne modifiée, aucune exception -
        // l'ancien SaveLibraryItemAsync aurait lui INSÉRÉ une track fantôme pour un Id à 0.
        await Service.UpdateTrackImagePathAsync(0, @"C:\covers\ghost.jpg");
        await Service.UpdateTrackImagePathAsync(9999, @"C:\covers\ghost.jpg");

        var reloaded = (Track)(await Service.GetAllItemsTypeAsync(typeof(Track))).Single();
        Assert.Equal(string.Empty, reloaded.ImagePath);
    }

    [Fact]
    public async Task DeleteItems_RemovesTracks_AndTheirSceneLinks()
    {
        var sceneService = new SceneDataService(Db);
        var track = MakeTrack("Doomed");
        await Service.SaveLibraryItemAsync(track);
        await sceneService.SaveSceneTrackAsync(new SceneTrack { SceneId = 1, Track = track });

        var deleted = await Service.DeleteItemsAsync(typeof(Track), new[] { track.Id });

        Assert.Single(deleted);
        Assert.Equal("Doomed", deleted[0].Title);
        Assert.Empty(await Service.GetAllItemsTypeAsync(typeof(Track)));
        Assert.Empty(await sceneService.GetSceneTracksAsync(1));
    }

    [Fact]
    public async Task GetAllReferencedFilePaths_IncludesTrackAndSpellFilePathsAndImagePaths()
    {
        var track = MakeTrack("Tavern");
        track.ImagePath = @"C:\covers\tavern.jpg";
        await Service.SaveLibraryItemAsync(track);

        var spell = new Spell { Title = "Boule de feu", FilePath = @"C:\spells\fireball.mp3", ImagePath = @"C:\covers\fireball.jpg" };
        await Service.SaveLibraryItemAsync(spell);

        var referenced = await Service.GetAllReferencedFilePathsAsync();

        Assert.Contains(track.FilePath, referenced);
        Assert.Contains(track.ImagePath, referenced);
        Assert.Contains(spell.FilePath, referenced);
        Assert.Contains(spell.ImagePath, referenced);
    }

    [Fact]
    public async Task GetAllReferencedFilePaths_KeepsSharedFile_ButDropsPathOnlyUsedByDeletedTrack()
    {
        // Deux tracks partagent le même fichier physique (dédup par hash à l'import) ; une
        // troisième a un fichier qui lui est propre. Reproduit exactement le garde-fou utilisé par
        // LibraryTrackViewModel.DeleteSelectedItems avant de supprimer un fichier du disque : ne
        // jamais l'effacer tant qu'une autre track vivante le référence encore.
        var shared1 = MakeTrack("Shared1"); shared1.FilePath = @"C:\tracks\shared.mp3";
        var shared2 = MakeTrack("Shared2"); shared2.FilePath = @"C:\tracks\shared.mp3";
        var unique = MakeTrack("Unique"); unique.FilePath = @"C:\tracks\unique.mp3";
        await Service.SaveLibraryItemAsync(shared1);
        await Service.SaveLibraryItemAsync(shared2);
        await Service.SaveLibraryItemAsync(unique);

        await Service.DeleteItemsAsync(typeof(Track), new[] { shared1.Id, unique.Id });

        var referenced = await Service.GetAllReferencedFilePathsAsync();

        Assert.Contains(@"C:\tracks\shared.mp3", referenced);
        Assert.DoesNotContain(@"C:\tracks\unique.mp3", referenced);
    }

    [Fact]
    public async Task EnsureCategory_IsIdempotent_AndScopedByLibraryType()
    {
        await Service.EnsureCategoryAsync(typeof(Track), "Musique");
        await Service.EnsureCategoryAsync(typeof(Track), "Musique");
        await Service.EnsureCategoryAsync(typeof(Spell), "Nécromancie");

        Assert.Equal(new[] { "Musique" }, await Service.GetCategoryNamesAsync(typeof(Track)));
        Assert.Equal(new[] { "Nécromancie" }, await Service.GetCategoryNamesAsync(typeof(Spell)));
    }

    [Fact]
    public async Task RenameCategory_RetagsTracks_AndMergesIntoExistingTarget()
    {
        await Service.EnsureCategoryAsync(typeof(Track), "Zik");
        await Service.EnsureCategoryAsync(typeof(Track), "Musique");
        var track = MakeTrack("Song", category: "Zik");
        await Service.SaveLibraryItemAsync(track);

        await Service.RenameCategoryAsync(typeof(Track), "Zik", "Musique");

        // Fusion : pas de doublon de catégorie, et la track suit.
        Assert.Equal(new[] { "Musique" }, await Service.GetCategoryNamesAsync(typeof(Track)));
        var loaded = (await Service.GetAllItemsTypeAsync(typeof(Track))).OfType<Track>().Single();
        Assert.Equal("Musique", loaded.Category);
    }

    [Fact]
    public async Task DeleteCategory_DetachesTracks_WithoutDeletingThem()
    {
        await Service.EnsureCategoryAsync(typeof(Track), "Temp");
        var track = MakeTrack("Kept", category: "Temp");
        await Service.SaveLibraryItemAsync(track);

        await Service.DeleteCategoryAsync(typeof(Track), "Temp");

        Assert.Empty(await Service.GetCategoryNamesAsync(typeof(Track)));
        var loaded = (await Service.GetAllItemsTypeAsync(typeof(Track))).OfType<Track>().Single();
        Assert.Equal(string.Empty, loaded.Category);
    }
}

public class AppDatabaseSeedTests : DatabaseTestBase
{
    protected override IEnumerable<string> DefaultTrackCategories => new[] { "Musique", "Ambiance" };

    [Fact]
    public async Task FirstLaunch_SeedsDefaultTrackCategories()
    {
        var service = new LibraryDataService(Db);

        var categories = await service.GetCategoryNamesAsync(typeof(Track));

        Assert.Equal(new[] { "Ambiance", "Musique" }, categories); // tri alphabétique
        Assert.Empty(await service.GetCategoryNamesAsync(typeof(Spell)));
    }
}
