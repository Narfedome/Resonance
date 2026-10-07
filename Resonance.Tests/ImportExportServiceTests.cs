using System.IO.Compression;
using System.Text;
using System.Text.Json;
using Resonance.Data;
using Resonance.Models;
using Resonance.Models.ImportExport;
using Resonance.Models.Library;
using Resonance.Services;

namespace Resonance.Tests;

/// <summary>Copie le fichier source dans un dossier de test dédié - joue le rôle du FileService MAUI.</summary>
public class FakeTrackFileStore : ITrackFileStore
{
    public string StorageDirectory { get; } = Path.Combine(Path.GetTempPath(), $"dmpack-tests-store-{Guid.NewGuid():N}");
    public List<string> CopiedFiles { get; } = new();

    public FakeTrackFileStore() => Directory.CreateDirectory(StorageDirectory);

    public string CopyTrackToLocal(string originalFilePath)
    {
        var dest = Path.Combine(StorageDirectory, Guid.NewGuid().ToString("N") + Path.GetExtension(originalFilePath));
        File.Copy(originalFilePath, dest);
        CopiedFiles.Add(dest);
        return dest;
    }

    public string ReserveTrackPath(string extension)
    {
        var dest = Path.Combine(StorageDirectory, Guid.NewGuid().ToString("N") + extension);
        CopiedFiles.Add(dest);
        return dest;
    }

    public void Dispose()
    {
        try { Directory.Delete(StorageDirectory, recursive: true); } catch { /* best effort */ }
    }
}

/// <summary>
/// Délègue à une vraie LibraryDataService mais fait échouer le prochain SaveLibraryItemAsync(Track) —
/// simule un échec de persistance survenant après une copie de fichier déjà réussie, pour vérifier
/// qu'ImportTrackAsync nettoie le fichier orphelin plutôt que de le laisser traîner sur disque.
/// </summary>
public class TrackSaveFailingLibraryDataService : ILibraryDataService
{
    private readonly LibraryDataService _inner;
    public bool FailNextTrackSave { get; set; }

    public TrackSaveFailingLibraryDataService(LibraryDataService inner) => _inner = inner;

    public Task SaveLibraryItemAsync(LibraryItem item)
    {
        if (FailNextTrackSave && item is Track)
        {
            FailNextTrackSave = false;
            throw new InvalidOperationException("Échec simulé de sauvegarde.");
        }
        return _inner.SaveLibraryItemAsync(item);
    }

    public Task DeleteLibraryItem(LibraryItem item) => _inner.DeleteLibraryItem(item);
    public Task<List<Track>> DeleteAllTracksAsync() => _inner.DeleteAllTracksAsync();
    public Task<List<int>> GetItemIdsAsync(Type currentLibraryType, string? category) => _inner.GetItemIdsAsync(currentLibraryType, category);
    public Task<List<LibraryItem>> DeleteItemsAsync(Type currentLibraryType, IEnumerable<int> ids) => _inner.DeleteItemsAsync(currentLibraryType, ids);
    public Task<List<LibraryItem>> GetAllItemsTypeAsync(Type currentLibraryType) => _inner.GetAllItemsTypeAsync(currentLibraryType);
    public Task<List<LibraryItem>> GetItemsPageAsync(Type currentLibraryType, int skip, int take, string? category = null) => _inner.GetItemsPageAsync(currentLibraryType, skip, take, category);
    public Task<Track?> FindTrackByHashAsync(string hash, int excludeId) => _inner.FindTrackByHashAsync(hash, excludeId);
    public Task UpdateTrackImagePathAsync(int trackId, string imagePath) => _inner.UpdateTrackImagePathAsync(trackId, imagePath);
    public Task<int> CountTracksWithFilePathAsync(string filePath, int excludeId) => _inner.CountTracksWithFilePathAsync(filePath, excludeId);
    public Task<HashSet<string>> GetAllReferencedFilePathsAsync() => _inner.GetAllReferencedFilePathsAsync();
    public Task<List<string>> GetCategoryNamesAsync(Type currentLibraryType) => _inner.GetCategoryNamesAsync(currentLibraryType);
    public Task EnsureCategoryAsync(Type currentLibraryType, string name) => _inner.EnsureCategoryAsync(currentLibraryType, name);
    public Task RenameCategoryAsync(Type currentLibraryType, string oldName, string newName) => _inner.RenameCategoryAsync(currentLibraryType, oldName, newName);
    public Task DeleteCategoryAsync(Type currentLibraryType, string name) => _inner.DeleteCategoryAsync(currentLibraryType, name);
}

/// <summary>Construit un WAV PCM silencieux minimal mais réellement décodable par TagLibSharp.</summary>
public static class TestAudioFile
{
    public static void WriteSilentWav(string path, double durationSeconds = 0.3)
    {
        const int sampleRate = 8000;
        const short bitsPerSample = 16;
        const short numChannels = 1;
        var data = new byte[(int)(sampleRate * durationSeconds) * numChannels * (bitsPerSample / 8)];

        var byteRate = sampleRate * numChannels * bitsPerSample / 8;
        var blockAlign = (short)(numChannels * bitsPerSample / 8);
        var chunkSize = 36 + data.Length;

        using var fs = File.Create(path);
        using var bw = new BinaryWriter(fs);
        bw.Write("RIFF"u8.ToArray());
        bw.Write(chunkSize);
        bw.Write("WAVE"u8.ToArray());
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);
        bw.Write((short)1);
        bw.Write(numChannels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write(blockAlign);
        bw.Write(bitsPerSample);
        bw.Write("data"u8.ToArray());
        bw.Write(data.Length);
        bw.Write(data);
    }
}

/// <summary>Une base SQLite temporaire + ses services, prête pour l'export ou l'import.</summary>
public sealed class ImportExportTestContext : IAsyncDisposable
{
    public required AppDatabase Db { get; init; }
    public required SceneDataService Scenes { get; init; }
    public required LibraryDataService Library { get; init; }
    public required FakeTrackFileStore Store { get; init; }
    public required ImportExportService Service { get; init; }
    public required string DbPath { get; init; }

    public static async Task<ImportExportTestContext> CreateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmpack-tests-{Guid.NewGuid():N}.db3");
        var db = new AppDatabase(path);
        await db.Initialization;
        var scenes = new SceneDataService(db);
        var library = new LibraryDataService(db);
        var store = new FakeTrackFileStore();
        return new ImportExportTestContext
        {
            Db = db,
            Scenes = scenes,
            Library = library,
            Store = store,
            Service = new ImportExportService(scenes, library, store),
            DbPath = path
        };
    }

    public async ValueTask DisposeAsync()
    {
        await Db.Connection.CloseAsync();
        try { File.Delete(DbPath); } catch { /* best effort */ }
        Store.Dispose();
    }
}

public class ImportExportServiceTests : IDisposable
{
    private readonly List<string> _tempAudioFiles = new();

    private string CreateWavFile(double durationSeconds = 0.3)
    {
        var path = Path.Combine(Path.GetTempPath(), $"dmpack-tests-audio-{Guid.NewGuid():N}.wav");
        TestAudioFile.WriteSilentWav(path, durationSeconds);
        _tempAudioFiles.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var path in _tempAudioFiles)
        {
            try { File.Delete(path); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task ExportImport_StructureOnly_RecreatesHierarchy_WithoutTouchingAudio()
    {
        await using var ctx = await ImportExportTestContext.CreateAsync();

        var campaign = new Campaign { Title = "Curse of Strahd" };
        await ctx.Scenes.SaveCampaignAsync(campaign);
        var session = new Session { CampaignId = campaign.Id, Title = "Chapitre 1" };
        await ctx.Scenes.SaveSessionAsync(session);
        var scene = new Scene { SessionId = session.Id, Title = "Taverne" };
        await ctx.Scenes.SaveSceneAsync(scene);

        using var zipStream = new MemoryStream();
        await ctx.Service.ExportAsync(new ExportRequest { Level = ExportLevel.StructureOnly, CampaignId = campaign.Id }, zipStream);

        zipStream.Position = 0;
        var result = await ctx.Service.ImportAsync(zipStream);

        Assert.Equal(1, result.CampaignsImported);
        Assert.Equal(0, result.TracksCopied);
        Assert.Equal(0, result.TracksReused);

        var allCampaigns = await ctx.Scenes.GetCampaignsAsync();
        Assert.Equal(2, allCampaigns.Count);
        var imported = allCampaigns.Single(c => c.Id != campaign.Id);
        Assert.Equal("Curse of Strahd", imported.Title);

        var importedSessions = await ctx.Scenes.GetSessionsAsync(imported.Id);
        var importedScenes = await ctx.Scenes.GetScenesAsync(importedSessions.Single().Id);
        Assert.Equal("Taverne", importedScenes.Single().Title);
        Assert.Empty(await ctx.Scenes.GetSceneTracksAsync(importedScenes.Single().Id));
    }

    [Fact]
    public async Task ExportImport_StructureWithChannels_SameLibrary_ReusesExistingTrackByHash()
    {
        await using var ctx = await ImportExportTestContext.CreateAsync();
        var wavPath = CreateWavFile();

        var track = new Track { Title = "Pluie", FilePath = wavPath, Hash = TrackTagHelper.ComputeSha256(wavPath) };
        await ctx.Library.SaveLibraryItemAsync(track);

        var campaign = new Campaign { Title = "C" };
        await ctx.Scenes.SaveCampaignAsync(campaign);
        var session = new Session { CampaignId = campaign.Id, Title = "S" };
        await ctx.Scenes.SaveSessionAsync(session);
        var scene = new Scene { SessionId = session.Id, Title = "Scene" };
        await ctx.Scenes.SaveSceneAsync(scene);
        await ctx.Scenes.SaveSceneTrackAsync(new SceneTrack
        {
            SceneId = scene.Id, Track = track, Position = 2, Volume = 0.42,
            AutoPlay = true, IsLooping = false, FadeIn = true, FadeOut = false
        });

        using var zipStream = new MemoryStream();
        await ctx.Service.ExportAsync(new ExportRequest { Level = ExportLevel.StructureWithChannels, CampaignId = campaign.Id }, zipStream);

        zipStream.Position = 0;
        var result = await ctx.Service.ImportAsync(zipStream);

        Assert.Equal(1, result.TracksReused);
        Assert.Equal(0, result.TracksCopied);
        Assert.Empty(ctx.Store.CopiedFiles);

        // La bibliothèque ne contient toujours qu'une seule track (dédup par hash).
        Assert.Single(await ctx.Library.GetAllItemsTypeAsync(typeof(Track)));

        var importedCampaign = (await ctx.Scenes.GetCampaignsAsync()).Single(c => c.Id != campaign.Id);
        var importedSession = (await ctx.Scenes.GetSessionsAsync(importedCampaign.Id)).Single();
        var importedScene = (await ctx.Scenes.GetScenesAsync(importedSession.Id)).Single();
        var importedSceneTrack = (await ctx.Scenes.GetSceneTracksAsync(importedScene.Id)).Single();

        Assert.Equal(track.Id, importedSceneTrack.Track.Id);
        Assert.Equal(0.42, importedSceneTrack.Volume);
        Assert.True(importedSceneTrack.AutoPlay);
        Assert.False(importedSceneTrack.IsLooping);
        Assert.True(importedSceneTrack.FadeIn);
        Assert.False(importedSceneTrack.FadeOut);
    }

    [Fact]
    public async Task ExportImport_StructureWithChannels_DifferentLibrary_CopiesFile_AndDedupsOnReimport()
    {
        await using var source = await ImportExportTestContext.CreateAsync();
        var wavPath = CreateWavFile();

        var track = new Track { Title = "Orage", FilePath = wavPath, Hash = TrackTagHelper.ComputeSha256(wavPath) };
        await source.Library.SaveLibraryItemAsync(track);

        var campaign = new Campaign { Title = "C" };
        await source.Scenes.SaveCampaignAsync(campaign);
        var session = new Session { CampaignId = campaign.Id, Title = "S" };
        await source.Scenes.SaveSessionAsync(session);
        var scene = new Scene { SessionId = session.Id, Title = "Scene" };
        await source.Scenes.SaveSceneAsync(scene);
        await source.Scenes.SaveSceneTrackAsync(new SceneTrack { SceneId = scene.Id, Track = track });

        using var zipStream = new MemoryStream();
        await source.Service.ExportAsync(new ExportRequest { Level = ExportLevel.StructureWithChannels, CampaignId = campaign.Id }, zipStream);

        await using var dest = await ImportExportTestContext.CreateAsync();

        zipStream.Position = 0;
        var firstImport = await dest.Service.ImportAsync(zipStream);
        Assert.Equal(1, firstImport.TracksCopied);
        Assert.Equal(0, firstImport.TracksReused);
        Assert.Single(dest.Store.CopiedFiles);
        Assert.True(File.Exists(dest.Store.CopiedFiles[0]));

        // Réimporter le même fichier ne doit pas dupliquer la track côté destination.
        zipStream.Position = 0;
        var secondImport = await dest.Service.ImportAsync(zipStream);
        Assert.Equal(0, secondImport.TracksCopied);
        Assert.Equal(1, secondImport.TracksReused);
        Assert.Single(dest.Store.CopiedFiles);

        Assert.Single(await dest.Library.GetAllItemsTypeAsync(typeof(Track)));
        Assert.Equal(2, (await dest.Scenes.GetCampaignsAsync()).Count);
    }

    [Fact]
    public async Task ExportImport_AudioLibraryOnly_ImportsAllTracks_WithoutCampaigns()
    {
        await using var source = await ImportExportTestContext.CreateAsync();
        var track1 = new Track { Title = "T1", FilePath = CreateWavFile(0.2) };
        track1.Hash = TrackTagHelper.ComputeSha256(track1.FilePath);
        var track2 = new Track { Title = "T2", FilePath = CreateWavFile(0.4) };
        track2.Hash = TrackTagHelper.ComputeSha256(track2.FilePath);
        await source.Library.SaveLibraryItemAsync(track1);
        await source.Library.SaveLibraryItemAsync(track2);

        using var zipStream = new MemoryStream();
        await source.Service.ExportAsync(new ExportRequest { Level = ExportLevel.AudioLibraryOnly }, zipStream);

        await using var dest = await ImportExportTestContext.CreateAsync();
        zipStream.Position = 0;
        var result = await dest.Service.ImportAsync(zipStream);

        Assert.Equal(0, result.CampaignsImported);
        Assert.Equal(2, result.TracksCopied);
        Assert.Equal(2, (await dest.Library.GetAllItemsTypeAsync(typeof(Track))).Count);
        Assert.Empty(await dest.Scenes.GetCampaignsAsync());
    }

    [Fact]
    public async Task ExportImport_PreservesOriginalTrackOrder()
    {
        // La phase de vérification de décodabilité (ImportTracksAsync) tourne en parallèle : un
        // ConcurrentBag y écrivait autrefois les résultats dans un ordre dépendant du hasard de la
        // concurrence, mélangeant l'ordre de sauvegarde (donc des Id, dont dépend l'affichage de la
        // bibliothèque) par rapport au manifeste d'origine. 12 pistes pour donner une vraie chance
        // aux 4 tâches parallèles de se terminer dans le désordre si la régression revenait.
        await using var source = await ImportExportTestContext.CreateAsync();
        var titles = new List<string>();
        for (int i = 0; i < 12; i++)
        {
            var title = $"Track {i:D2}";
            titles.Add(title);
            var wavPath = CreateWavFile(0.1);
            var track = new Track { Title = title, FilePath = wavPath, Hash = TrackTagHelper.ComputeSha256(wavPath) };
            await source.Library.SaveLibraryItemAsync(track);
        }

        using var zipStream = new MemoryStream();
        await source.Service.ExportAsync(new ExportRequest { Level = ExportLevel.AudioLibraryOnly }, zipStream);

        await using var dest = await ImportExportTestContext.CreateAsync();
        zipStream.Position = 0;
        var result = await dest.Service.ImportAsync(zipStream);

        Assert.Equal(titles, result.ImportedTracks.Select(t => t.Title).ToList());
    }

    [Fact]
    public async Task ExportImport_AudioLibraryOnly_RegistersCustomTrackCategory()
    {
        // manifest.Library.Categories n'est rempli qu'en FullBackup : ce niveau d'export est
        // justement celui qui ne passe pas par là, pour vérifier que la catégorie custom d'une
        // track est quand même enregistrée (et pas juste posée en texte sur la track importée).
        await using var source = await ImportExportTestContext.CreateAsync();
        var wavPath = CreateWavFile();
        var track = new Track { Title = "Machin", Category = "Bidule", FilePath = wavPath, Hash = TrackTagHelper.ComputeSha256(wavPath) };
        await source.Library.SaveLibraryItemAsync(track);

        using var zipStream = new MemoryStream();
        await source.Service.ExportAsync(new ExportRequest { Level = ExportLevel.AudioLibraryOnly }, zipStream);

        await using var dest = await ImportExportTestContext.CreateAsync();
        zipStream.Position = 0;
        var result = await dest.Service.ImportAsync(zipStream);

        Assert.Equal(1, result.TracksCopied);
        var imported = (await dest.Library.GetAllItemsTypeAsync(typeof(Track))).OfType<Track>().Single();
        Assert.Equal("Bidule", imported.Category);
        Assert.Contains("Bidule", await dest.Library.GetCategoryNamesAsync(typeof(Track)));
    }

    [Fact]
    public async Task ExportImport_FullBackup_IncludesAllCampaignsAndLibrary()
    {
        await using var source = await ImportExportTestContext.CreateAsync();

        var c1 = new Campaign { Title = "C1" };
        await source.Scenes.SaveCampaignAsync(c1);
        await source.Scenes.SaveSessionAsync(new Session { CampaignId = c1.Id, Title = "S1" });
        var c2 = new Campaign { Title = "C2" };
        await source.Scenes.SaveCampaignAsync(c2);
        await source.Scenes.SaveSessionAsync(new Session { CampaignId = c2.Id, Title = "S2" });

        await source.Library.SaveLibraryItemAsync(new Spell { Title = "Boule de feu", Description = "..." });
        await source.Library.EnsureCategoryAsync(typeof(Track), "Ambiance");

        using var zipStream = new MemoryStream();
        await source.Service.ExportAsync(new ExportRequest { Level = ExportLevel.FullBackup }, zipStream);

        await using var dest = await ImportExportTestContext.CreateAsync();
        zipStream.Position = 0;
        var result = await dest.Service.ImportAsync(zipStream);

        Assert.Equal(2, result.CampaignsImported);
        Assert.Equal(1, result.SpellsImported);
        Assert.Contains("Ambiance", await dest.Library.GetCategoryNamesAsync(typeof(Track)));
    }

    [Fact]
    public async Task Import_RejectsEntryWithPathOutsideExpectedLayout()
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var evil = zip.CreateEntry("../../evil.txt", CompressionLevel.NoCompression);
            using var s = evil.Open();
            s.Write(Encoding.UTF8.GetBytes("payload"));
        }

        await using var ctx = await ImportExportTestContext.CreateAsync();
        zipStream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => ctx.Service.ImportAsync(zipStream));
    }

    /// <summary>Écrit manifest.json + une signature manifest.sig valide, comme le ferait un vrai export.</summary>
    private static void WriteSignedManifest(ZipArchive zip, ExportManifest manifest)
    {
        var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest);

        var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
        using (var s = manifestEntry.Open())
            s.Write(manifestBytes);

        var signatureEntry = zip.CreateEntry("manifest.sig", CompressionLevel.Optimal);
        using (var s = signatureEntry.Open())
            s.Write(Encoding.ASCII.GetBytes(Convert.ToHexString(ImportExportService.ComputeManifestSignature(manifestBytes))));
    }

    [Fact]
    public async Task Import_RejectsManifestWithUnsupportedFormatVersion()
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteSignedManifest(zip, new ExportManifest { FormatVersion = 99, ExportLevel = 1 });
        }

        await using var ctx = await ImportExportTestContext.CreateAsync();
        zipStream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => ctx.Service.ImportAsync(zipStream));
    }

    [Fact]
    public async Task Import_RejectsManifestTamperedAfterSigning()
    {
        await using var source = await ImportExportTestContext.CreateAsync();
        var campaign = new Campaign { Title = "Original" };
        await source.Scenes.SaveCampaignAsync(campaign);

        using var zipStream = new MemoryStream();
        await source.Service.ExportAsync(new ExportRequest { Level = ExportLevel.StructureOnly, CampaignId = campaign.Id }, zipStream);

        // Modifie manifest.json après coup (ex. via un logiciel de zip) sans recalculer manifest.sig :
        // c'est exactement ce que la signature doit détecter. Flux redimensionnable (contrairement à
        // MemoryStream(byte[])) car le nouveau titre peut agrandir l'archive.
        using var tampered = new MemoryStream();
        zipStream.Position = 0;
        zipStream.CopyTo(tampered);
        using (var zip = new ZipArchive(tampered, ZipArchiveMode.Update, leaveOpen: true))
        {
            var manifestEntry = zip.GetEntry("manifest.json")!;
            ExportManifest manifest;
            using (var s = manifestEntry.Open())
                manifest = (await JsonSerializer.DeserializeAsync<ExportManifest>(s))!;
            manifest.Campaigns[0].Title = "Titre modifié après signature";

            manifestEntry.Delete();
            var replacement = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var w = replacement.Open();
            await JsonSerializer.SerializeAsync(w, manifest);
        }

        await using var dest = await ImportExportTestContext.CreateAsync();
        tampered.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => dest.Service.ImportAsync(tampered));
    }

    [Fact]
    public async Task Import_RejectsEntryExceedingSizeCap()
    {
        using var zipStream = new MemoryStream();
        using (var zip = new ZipArchive(zipStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var hash = new string('a', 64);
            var entry = zip.CreateEntry($"tracks/{hash}.wav", CompressionLevel.Optimal);
            using (var entryStream = entry.Open())
            {
                // Zéros hautement compressibles : le zip reste petit sur disque/mémoire mais déclare
                // une taille décompressée qui dépasse le plafond anti zip-bomb du service (500 Mo).
                var buffer = new byte[1024 * 1024];
                const long targetBytes = 500L * 1024 * 1024 + (1024 * 1024);
                long written = 0;
                while (written < targetBytes)
                {
                    await entryStream.WriteAsync(buffer);
                    written += buffer.Length;
                }
            }

            var manifestEntry = zip.CreateEntry("manifest.json", CompressionLevel.Optimal);
            using var manifestStream = manifestEntry.Open();
            await JsonSerializer.SerializeAsync(manifestStream, new ExportManifest { FormatVersion = 1, ExportLevel = 3 });
        }

        await using var ctx = await ImportExportTestContext.CreateAsync();
        zipStream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(() => ctx.Service.ImportAsync(zipStream));
    }

    [Fact]
    public async Task Import_RejectsTrackWhoseContentDoesNotMatchDeclaredHash()
    {
        await using var source = await ImportExportTestContext.CreateAsync();
        var wavPath = CreateWavFile();
        var track = new Track { Title = "X", FilePath = wavPath, Hash = TrackTagHelper.ComputeSha256(wavPath) };
        await source.Library.SaveLibraryItemAsync(track);

        using var zipStream = new MemoryStream();
        await source.Service.ExportAsync(new ExportRequest { Level = ExportLevel.AudioLibraryOnly }, zipStream);

        // Trafique le contenu de l'entrée audio (le nom, dérivé du hash déclaré, reste valide face au
        // pattern attendu — seul le contenu réel diverge du hash annoncé dans le manifeste).
        using var tamperedStream = new MemoryStream(zipStream.ToArray());
        using (var zip = new ZipArchive(tamperedStream, ZipArchiveMode.Update, leaveOpen: true))
        {
            var entry = zip.Entries.Single(e => e.FullName.StartsWith("tracks/"));
            var name = entry.FullName;
            entry.Delete();
            var replacement = zip.CreateEntry(name, CompressionLevel.NoCompression);
            using var s = replacement.Open();
            s.Write(Encoding.UTF8.GetBytes("ceci n'est pas le bon fichier audio"));
        }

        await using var dest = await ImportExportTestContext.CreateAsync();
        tamperedStream.Position = 0;
        var result = await dest.Service.ImportAsync(tamperedStream);

        Assert.Equal(1, result.TracksRejected);
        Assert.Equal(0, result.TracksCopied);
        Assert.Empty(await dest.Library.GetAllItemsTypeAsync(typeof(Track)));
    }

    [Fact]
    public async Task Import_CleansUpCopiedFile_WhenPersistingTrackFails()
    {
        await using var source = await ImportExportTestContext.CreateAsync();
        var wavPath = CreateWavFile();
        var track = new Track { Title = "Fragile", FilePath = wavPath, Hash = TrackTagHelper.ComputeSha256(wavPath) };
        await source.Library.SaveLibraryItemAsync(track);

        using var zipStream = new MemoryStream();
        await source.Service.ExportAsync(new ExportRequest { Level = ExportLevel.AudioLibraryOnly }, zipStream);

        await using var dest = await ImportExportTestContext.CreateAsync();
        var failingLibrary = new TrackSaveFailingLibraryDataService(dest.Library) { FailNextTrackSave = true };
        var service = new ImportExportService(dest.Scenes, failingLibrary, dest.Store);

        zipStream.Position = 0;
        var result = await service.ImportAsync(zipStream);

        Assert.Equal(1, result.TracksRejected);
        Assert.Equal(0, result.TracksCopied);
        Assert.Single(dest.Store.CopiedFiles);
        Assert.False(File.Exists(dest.Store.CopiedFiles[0]));
    }
}
