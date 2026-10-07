using Resonance.Models.Library;

namespace Resonance.Tests;

public class LibraryItemModelTests
{
    private static Track MakeTrack() => new()
    {
        Id = 42,
        Title = "Tavern Ambience",
        ImagePath = @"C:\assets\tavern.jpg",
        FilePath = @"C:\tracks\tavern.mp3",
        Duration = TimeSpan.FromSeconds(95),
        Volume = 0.7,
        Hash = "ABC123",
        Category = "Ambiance"
    };

    [Fact]
    public void Clone_CopiesAllTrackFields()
    {
        var original = MakeTrack();

        var clone = (Track)original.Clone();

        Assert.Equal(original.Id, clone.Id);
        Assert.Equal(original.Title, clone.Title);
        Assert.Equal(original.ImagePath, clone.ImagePath);
        Assert.Equal(original.FilePath, clone.FilePath);
        Assert.Equal(original.Duration, clone.Duration);
        Assert.Equal(original.Volume, clone.Volume);
        Assert.Equal(original.Hash, clone.Hash);
        Assert.Equal(original.Category, clone.Category);
    }

    [Fact]
    public void Clone_PreservesRuntimeType()
    {
        LibraryItem track = MakeTrack();
        LibraryItem spell = new Spell { Id = 1, Title = "Fireball", Description = "Boom" };

        Assert.IsType<Track>(track.Clone());
        Assert.IsType<Spell>(spell.Clone());
        Assert.Equal("Boom", ((Spell)spell.Clone()).Description);
    }

    // Le flux d'édition (EditItem) repose sur ce contrat : on édite une copie, et l'original ne
    // doit pas bouger tant que l'utilisateur n'a pas validé.
    [Fact]
    public void Clone_MutationsDoNotAffectOriginal()
    {
        var original = MakeTrack();
        var clone = (Track)original.Clone();

        clone.Title = "Edited";
        clone.Volume = 0.1;
        clone.Category = "Musique";

        Assert.Equal("Tavern Ambience", original.Title);
        Assert.Equal(0.7, original.Volume);
        Assert.Equal("Ambiance", original.Category);
    }

    [Fact]
    public void DurationFormatted_UsesMinutesSeconds_UnderOneHour()
    {
        var track = new Track { Duration = new TimeSpan(0, 3, 7) };

        Assert.Equal("03:07", track.DurationFormatted);
    }

    [Fact]
    public void DurationFormatted_UsesHours_FromOneHour()
    {
        var track = new Track { Duration = new TimeSpan(1, 2, 3) };

        Assert.Equal("01:02:03", track.DurationFormatted);
    }

    // L'UI (tuiles de la bibliothèque, page des pistes de scène) est bindée sur DurationFormatted :
    // elle doit être notifiée quand Duration change, sinon l'affichage reste figé.
    [Fact]
    public void DurationChange_NotifiesDurationFormatted()
    {
        var track = new Track();
        var notified = new List<string?>();
        track.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        track.Duration = TimeSpan.FromMinutes(2);

        Assert.Contains(nameof(Track.DurationFormatted), notified);
    }
}
