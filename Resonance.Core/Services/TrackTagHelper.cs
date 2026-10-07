namespace Resonance.Services
{
    /// <summary>
    /// Logique pure (sans dépendance MAUI) d'extraction de métadonnées audio, extraite de FileService
    /// pour rester testable dans un projet de tests sans hôte MAUI.
    /// </summary>
    public static class TrackTagHelper
    {
        public static string ComputeSha256(string filePath)
        {
            using var sha = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            return Convert.ToHexString(sha.ComputeHash(stream));
        }

        /// <summary>
        /// Vérifie qu'un fichier est un audio réellement décodable (utilisé à l'import d'un .dmpack :
        /// un fichier reçu d'un tiers ne doit jamais être accepté en bibliothèque sur la seule foi de
        /// son extension). ReadStyle.None : la détection de format (ce qui nous intéresse ici) se fait
        /// indépendamment du ReadStyle, seul le calcul de Properties (bitrate moyen, durée précise) est
        /// sauté - un scan complet du flux audio, inutile ici puisque ImportExportService utilise déjà
        /// la durée déclarée dans le manifeste plutôt que de la recalculer. Se fier uniquement à
        /// l'absence d'exception (format reconnu) plutôt qu'à Duration > 0 (jamais renseignée sous
        /// ReadStyle.None) : le hash SHA256 déjà vérifié avant cet appel garantit que le contenu
        /// correspond exactement au fichier original exporté, un faux fichier ne le passerait pas.
        /// </summary>
        public static bool IsDecodableAudio(string filePath)
        {
            try
            {
                using var tagFile = TagLib.File.Create(filePath, TagLib.ReadStyle.None);
                return true;
            }
            catch (TagLib.UnsupportedFormatException)
            {
                return false;
            }
            catch (TagLib.CorruptFileException)
            {
                return false;
            }
        }

        /// <summary>
        /// Construit le titre d'une piste à partir de ses tags audio : "Artiste - Titre" si les deux sont
        /// présents, juste le titre si l'artiste manque, ou le nom de fichier si aucun titre n'est taggé.
        /// </summary>
        public static string ExtractTitle(TagLib.Tag tag, string fallbackFileName)
        {
            var title = tag.Title?.Trim();
            if (string.IsNullOrEmpty(title))
                return fallbackFileName;

            var artist = tag.FirstAlbumArtist?.Trim();
            return string.IsNullOrEmpty(artist) ? title : $"{artist} - {title}";
        }
    }
}
