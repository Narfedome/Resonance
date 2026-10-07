using Resonance.Data.Entities;
using Resonance.Models;
using Resonance.Models.Library;

namespace Resonance.Data
{
    /// <summary>
    /// Conversions entités SQLite ↔ modèles, centralisées : un champ ajouté ne se mappe qu'ici
    /// (les blocs de mapping étaient dupliqués dans les services de données, et chaque nouveau
    /// réglage — Hash, Category, FadeIn, FadeOut... — risquait un oubli silencieux), et le
    /// round-trip est couvert par des tests unitaires.
    /// </summary>
    public static class EntityMapping
    {
        public static Track ToModel(this TrackEntity e) => new()
        {
            Id = e.Id,
            Title = e.Title,
            ImagePath = e.ImagePath,
            FilePath = e.FilePath,
            Duration = e.Duration,
            Volume = e.DefaultVolume,
            Hash = e.Hash,
            Category = e.Category
        };

        public static TrackEntity ToEntity(this Track m) => new()
        {
            Id = m.Id,
            Title = m.Title,
            ImagePath = m.ImagePath,
            FilePath = m.FilePath,
            Duration = m.Duration,
            DefaultVolume = m.Volume,
            Hash = m.Hash,
            Category = m.Category
        };

        public static Spell ToModel(this SpellEntity e) => new()
        {
            Id = e.Id,
            Title = e.Title,
            ImagePath = e.ImagePath,
            FilePath = e.FilePath,
            Description = e.Description
        };

        public static SpellEntity ToEntity(this Spell m) => new()
        {
            Id = m.Id,
            Title = m.Title,
            ImagePath = m.ImagePath,
            FilePath = m.FilePath,
            Description = m.Description
        };

        public static Campaign ToModel(this CampaignEntity e) => new() { Id = e.Id, Title = e.Title, Position = e.Position };

        public static CampaignEntity ToEntity(this Campaign m) => new() { Id = m.Id, Title = m.Title, Position = m.Position };

        public static Session ToModel(this SessionEntity e) => new()
        {
            Id = e.Id,
            CampaignId = e.CampaignId,
            Title = e.Title,
            Position = e.Position
        };

        public static SessionEntity ToEntity(this Session m) => new()
        {
            Id = m.Id,
            CampaignId = m.CampaignId,
            Title = m.Title,
            Position = m.Position
        };

        public static Scene ToModel(this SceneEntity e) => new()
        {
            Id = e.Id,
            SessionId = e.SessionId,
            Title = e.Title,
            Position = e.Position
        };

        public static SceneEntity ToEntity(this Scene m) => new()
        {
            Id = m.Id,
            SessionId = m.SessionId,
            Title = m.Title,
            Position = m.Position
        };

        /// <param name="track">Piste jointe (chargée séparément — SQLite-net ne fait pas de jointure).</param>
        public static SceneTrack ToModel(this SceneTrackEntity e, Track track) => new()
        {
            Id = e.Id,
            SceneId = e.SceneId,
            Position = e.Position,
            Volume = e.Volume,
            AutoPlay = e.AutoPlay,
            IsLooping = e.IsLooping,
            FadeIn = e.FadeIn,
            FadeOut = e.FadeOut,
            Track = track
        };

        public static SceneTrackEntity ToEntity(this SceneTrack m) => new()
        {
            Id = m.Id,
            SceneId = m.SceneId,
            TrackId = m.Track.Id,
            Position = m.Position,
            Volume = m.Volume,
            AutoPlay = m.AutoPlay,
            IsLooping = m.IsLooping,
            FadeIn = m.FadeIn,
            FadeOut = m.FadeOut
        };
    }
}
