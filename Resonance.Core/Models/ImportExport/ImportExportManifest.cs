namespace Resonance.Models.ImportExport
{
    /// <summary>
    /// Racine du fichier manifest.json d'un export .dmpack. Les Id repris ici sont ceux de la base
    /// source, valables uniquement à l'intérieur de ce fichier : ils servent à relier les entrées du
    /// manifeste entre elles (ex. SceneTrackExport.TrackId → TrackExport.Id), pas à identifier quoi
    /// que ce soit côté base de destination. L'import les remappe systématiquement vers de nouveaux
    /// Id locaux.
    /// </summary>
    public class ExportManifest
    {
        public int FormatVersion { get; set; } = 1;
        public int ExportLevel { get; set; }
        public DateTime ExportedAt { get; set; }
        public List<CampaignExport> Campaigns { get; set; } = new();
        public List<TrackExport> Tracks { get; set; } = new();
        public LibraryExport? Library { get; set; }
    }

    public class CampaignExport
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<SessionExport> Sessions { get; set; } = new();
    }

    public class SessionExport
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<SceneExport> Scenes { get; set; } = new();
    }

    public class SceneExport
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<SceneTrackExport> SceneTracks { get; set; } = new();
    }

    public class SceneTrackExport
    {
        public int TrackId { get; set; }
        public double Volume { get; set; }
        public int Position { get; set; }
        public bool AutoPlay { get; set; }
        public bool IsLooping { get; set; }
        public bool FadeIn { get; set; }
        public bool FadeOut { get; set; }
    }

    /// <summary>
    /// Métadonnées d'une track référencée par l'export. <see cref="FileEntry"/> pointe vers l'entrée
    /// du zip ("tracks/&lt;hash&gt;.ext") ; le nom est toujours dérivé du hash, jamais du nom de
    /// fichier original, pour ne jamais faire confiance à un chemin arbitraire à l'extraction.
    /// </summary>
    public class TrackExport
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public double DurationSeconds { get; set; }
        public double DefaultVolume { get; set; }
        public string Hash { get; set; } = string.Empty;
        public string FileEntry { get; set; } = string.Empty;
    }

    public class LibraryExport
    {
        public List<SpellExport> Spells { get; set; } = new();
        public List<CategoryExport> Categories { get; set; } = new();
    }

    public class SpellExport
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
    }

    public class CategoryExport
    {
        public string Name { get; set; } = string.Empty;
        public string LibraryType { get; set; } = string.Empty;
    }
}
