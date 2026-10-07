using Resonance.Services;

namespace Resonance.Tests;

public class TrackTagHelperTests
{
    [Fact]
    public void ExtractTitle_UsesArtistAndTitle_WhenBothPresent()
    {
        var tag = new TagLib.Id3v2.Tag { Title = "Song", AlbumArtists = new[] { "Artist" } };

        var result = TrackTagHelper.ExtractTitle(tag, "fallback.mp3");

        Assert.Equal("Artist - Song", result);
    }

    [Fact]
    public void ExtractTitle_UsesTitleOnly_WhenArtistMissing()
    {
        var tag = new TagLib.Id3v2.Tag { Title = "Song" };

        var result = TrackTagHelper.ExtractTitle(tag, "fallback.mp3");

        Assert.Equal("Song", result);
    }

    [Fact]
    public void ExtractTitle_FallsBackToFileName_WhenNoTitleTag()
    {
        var tag = new TagLib.Id3v2.Tag { AlbumArtists = new[] { "Artist" } };

        var result = TrackTagHelper.ExtractTitle(tag, "fallback.mp3");

        Assert.Equal("fallback.mp3", result);
    }

    [Fact]
    public void ExtractTitle_TrimsWhitespace()
    {
        var tag = new TagLib.Id3v2.Tag { Title = "  Song  ", AlbumArtists = new[] { "  Artist  " } };

        var result = TrackTagHelper.ExtractTitle(tag, "fallback.mp3");

        Assert.Equal("Artist - Song", result);
    }

    [Fact]
    public void ComputeSha256_MatchesKnownVector()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "hello");

            var hash = TrackTagHelper.ComputeSha256(path);

            Assert.Equal("2CF24DBA5FB0A30E26E83B2AC5B9E29E1B161E5C1FA7425E73043362938B9824", hash);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ComputeSha256_IsStableForIdenticalContent_AndDiffersOtherwise()
    {
        var pathA = Path.GetTempFileName();
        var pathB = Path.GetTempFileName();
        try
        {
            File.WriteAllText(pathA, "same content");
            File.WriteAllText(pathB, "same content");
            var hashA = TrackTagHelper.ComputeSha256(pathA);
            var hashB = TrackTagHelper.ComputeSha256(pathB);
            Assert.Equal(hashA, hashB);

            File.WriteAllText(pathB, "different content");
            var hashC = TrackTagHelper.ComputeSha256(pathB);
            Assert.NotEqual(hashA, hashC);
        }
        finally
        {
            File.Delete(pathA);
            File.Delete(pathB);
        }
    }

    // IsDecodableAudio est le dernier rempart avant d'accepter un fichier extrait d'un .dmpack en
    // bibliothèque (cf. ImportExportService) : un hash déclaré correct ne suffit pas, le contenu
    // doit être un audio réellement décodable. ReadStyle.None (pas de scan du bitrate moyen, coûteux
    // et inutile ici) : la détection de format se fait indépendamment du ReadStyle, ces deux tests
    // vérifient donc toujours le même comportement qu'avant ce changement.

    [Fact]
    public void IsDecodableAudio_ReturnsFalse_ForNonAudioFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllText(path, "ceci n'est pas un fichier audio");
            Assert.False(TrackTagHelper.IsDecodableAudio(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void IsDecodableAudio_ReturnsTrue_ForValidAudioFile()
    {
        var path = Path.Combine(Path.GetTempPath(), $"tth-tests-{Guid.NewGuid():N}.wav");
        try
        {
            TestAudioFile.WriteSilentWav(path);
            Assert.True(TrackTagHelper.IsDecodableAudio(path));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
