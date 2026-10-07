using Resonance.Data;
using Resonance.Data.Entities;
using Resonance.Models;
using Resonance.Models.Library;

namespace Resonance.Tests;

/// <summary>
/// Round-trips modèle → entité → modèle : c'est le filet contre la classe de bugs "j'ai ajouté un
/// champ (Hash, Category, FadeIn, FadeOut...) mais oublié un point de mapping", qui perdait des
/// réglages silencieusement.
/// </summary>
public class EntityMappingTests
{
    [Fact]
    public void Track_RoundTrip_PreservesAllFields()
    {
        var model = new Track
        {
            Id = 7,
            Title = "Battle Drums",
            ImagePath = @"C:\img\drums.jpg",
            FilePath = @"C:\tracks\drums.mp3",
            Duration = TimeSpan.FromSeconds(214),
            Volume = 0.65,
            Hash = "FEED",
            Category = "Musique"
        };

        var back = model.ToEntity().ToModel();

        Assert.Equal(model.Id, back.Id);
        Assert.Equal(model.Title, back.Title);
        Assert.Equal(model.ImagePath, back.ImagePath);
        Assert.Equal(model.FilePath, back.FilePath);
        Assert.Equal(model.Duration, back.Duration);
        Assert.Equal(model.Volume, back.Volume);
        Assert.Equal(model.Hash, back.Hash);
        Assert.Equal(model.Category, back.Category);
    }

    [Fact]
    public void SceneTrack_RoundTrip_PreservesAllSettings()
    {
        var track = new Track { Id = 3, Title = "Rain" };
        var model = new SceneTrack
        {
            Id = 11,
            SceneId = 5,
            Position = 2,
            Volume = 0.4,
            AutoPlay = true,
            IsLooping = false,
            FadeIn = true,
            FadeOut = true,
            Track = track
        };

        var entity = model.ToEntity();
        Assert.Equal(track.Id, entity.TrackId);

        var back = entity.ToModel(track);

        Assert.Equal(model.Id, back.Id);
        Assert.Equal(model.SceneId, back.SceneId);
        Assert.Equal(model.Position, back.Position);
        Assert.Equal(model.Volume, back.Volume);
        Assert.Equal(model.AutoPlay, back.AutoPlay);
        Assert.Equal(model.IsLooping, back.IsLooping);
        Assert.Equal(model.FadeIn, back.FadeIn);
        Assert.Equal(model.FadeOut, back.FadeOut);
        Assert.Same(track, back.Track);
    }

    [Fact]
    public void Spell_RoundTrip_PreservesAllFields()
    {
        var model = new Spell
        {
            Id = 9,
            Title = "Fireball",
            ImagePath = @"C:\img\fire.png",
            FilePath = @"C:\spells\fire.pdf",
            Description = "8d6 de dégâts de feu"
        };

        var back = model.ToEntity().ToModel();

        Assert.Equal(model.Id, back.Id);
        Assert.Equal(model.Title, back.Title);
        Assert.Equal(model.ImagePath, back.ImagePath);
        Assert.Equal(model.FilePath, back.FilePath);
        Assert.Equal(model.Description, back.Description);
    }

    [Fact]
    public void CampaignSessionScene_RoundTrips_PreserveFields()
    {
        var campaign = new Campaign { Id = 1, Title = "Curse of Strahd" };
        var session = new Session { Id = 2, CampaignId = 1, Title = "Chapitre 1" };
        var scene = new Scene { Id = 3, SessionId = 2, Title = "La taverne" };

        var campaignBack = campaign.ToEntity().ToModel();
        Assert.Equal(campaign.Id, campaignBack.Id);
        Assert.Equal(campaign.Title, campaignBack.Title);

        var sessionBack = session.ToEntity().ToModel();
        Assert.Equal(session.Id, sessionBack.Id);
        Assert.Equal(session.CampaignId, sessionBack.CampaignId);
        Assert.Equal(session.Title, sessionBack.Title);

        var sceneBack = scene.ToEntity().ToModel();
        Assert.Equal(scene.Id, sceneBack.Id);
        Assert.Equal(scene.SessionId, sceneBack.SessionId);
        Assert.Equal(scene.Title, sceneBack.Title);
    }
}
