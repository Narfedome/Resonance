using Resonance.Models.Library;

namespace Resonance.Models.ImportExport
{
    public enum ExportLevel
    {
        /// <summary>Campagne choisie : Campagne/Chapitre/Scène, sans canaux ni audio.</summary>
        StructureOnly = 1,

        /// <summary>Campagne choisie, avec les canaux (SceneTrack) et les fichiers audio référencés.</summary>
        StructureWithChannels = 2,

        /// <summary>Toute la bibliothèque audio, sans structure de campagne.</summary>
        AudioLibraryOnly = 3,

        /// <summary>Backup complet : toutes les campagnes (avec canaux) + toute la bibliothèque.</summary>
        FullBackup = 4
    }

    /// <summary>
    /// <see cref="CampaignId"/> est requis pour <see cref="ExportLevel.StructureOnly"/> et
    /// <see cref="ExportLevel.StructureWithChannels"/> ; ignoré pour les deux autres niveaux, qui ne
    /// portent pas sur une seule campagne.
    /// </summary>
    public class ExportRequest
    {
        public ExportLevel Level { get; set; }
        public int CampaignId { get; set; }
    }

    public class ExportProgress
    {
        public string CurrentItem { get; set; } = string.Empty;
        public int Processed { get; set; }
        public int Total { get; set; }
    }

    public class ImportProgress
    {
        public string CurrentItem { get; set; } = string.Empty;
        public int Processed { get; set; }
        public int Total { get; set; }

        // Extraction terminée (Processed==Total dès ce stade), mais la vérification de décodabilité
        // et la sauvegarde en base tournent encore : sans ce signal, l'appelant afficherait "Terminé"
        // (Processed/Total au max) pendant que du travail reste en cours. Pas de texte ici (Core ne
        // dépend pas de la localisation) - à l'appelant de traduire ce signal en message affiché.
        public bool IsVerifyingTracks { get; set; }
    }

    /// <summary>Résumé d'un import, affiché à l'utilisateur en fin d'opération.</summary>
    public class ImportResult
    {
        public int CampaignsImported { get; set; }
        public int TracksReused { get; set; }
        public int TracksCopied { get; set; }
        public int TracksRejected { get; set; }

        // Détail des causes de rejet (leur somme égale TracksRejected) : affiché à l'utilisateur
        // uniquement s'il y a effectivement des rejets, pour comprendre pourquoi sans avoir à
        // consulter des logs.
        public int TracksRejectedHashMismatch { get; set; }
        public int TracksRejectedNotDecodable { get; set; }
        public int TracksRejectedMissingEntry { get; set; }
        public int TracksRejectedOther { get; set; }

        public int SpellsImported { get; set; }

        // Pistes tout juste créées (Track.Id/FilePath renseignés), exposées pour que l'appelant
        // (App, jamais Core) puisse pré-chauffer leur pochette via CoverArtService après l'import -
        // Core ne dépend pas de MAUI, donc cette extraction ne peut pas se faire ici.
        public List<Track> ImportedTracks { get; } = new();

        // Durée par phase, affichée à l'utilisateur en fin d'import (utile pour se rendre compte du
        // temps réel passé, et pour nous remonter un ressenti de lenteur avec des chiffres concrets).
        public TimeSpan ExtractionDuration { get; set; }
        public TimeSpan VerificationDuration { get; set; }
        public TimeSpan DatabaseSaveDuration { get; set; }
        public TimeSpan TotalDuration { get; set; }
    }
}
