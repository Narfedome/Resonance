using Resonance.Models.Library;
using Microsoft.Maui.Graphics.Platform;
using System.Collections.Concurrent;

namespace Resonance.Services
{
    /// <summary>
    /// Résout la pochette d'une track pour l'affichage, avec un chemin rapide (fichier déjà
    /// extrait sur disque, référencé par Track.ImagePath) et un repli pour les pistes importées
    /// avant l'introduction de ce cache (extraction TagLib à la volée), qui persiste alors le
    /// résultat pour ne plus jamais avoir à le refaire (préchauffe progressive de la bibliothèque).
    /// </summary>
    public class CoverArtService
    {
        // Taille max (px) des vignettes de pochette - largement suffisant pour l'affichage 120x120,
        // avec un peu de marge pour les écrans haute densité, sans garder les images en pleine résolution.
        private const int CoverThumbnailSize = 160;

        // Limite le nombre d'extractions TagLib simultanées (repli legacy uniquement) : sans ça,
        // ouvrir la bibliothèque pourrait lancer une dizaine de parsings de fichiers volumineux en
        // même temps, ce qui sature le thread pool et le disque au lieu de charger en douceur.
        private static readonly SemaphoreSlim CoverLoadThrottle = new(3);

        private readonly ConcurrentDictionary<string, ImageSource?> _cache = new();

        private readonly FileService _fileService;
        private readonly ILibraryDataService _libraryDataService;

        public CoverArtService(FileService fileService, ILibraryDataService libraryDataService)
        {
            _fileService = fileService;
            _libraryDataService = libraryDataService;
        }

        /// <summary>
        /// Lecture synchrone du cache mémoire, pour éviter un flash "sans pochette" le temps qu'une
        /// tâche async se résolve alors que l'image est déjà connue.
        /// </summary>
        public bool TryGetCached(Track track, out ImageSource? image)
        {
            if (string.IsNullOrEmpty(track.FilePath))
            {
                image = null;
                return false;
            }

            return _cache.TryGetValue(track.FilePath, out image);
        }

        /// <summary>
        /// Charge (ou lit depuis le cache) la pochette d'une track. Utilisé aussi bien par la tuile
        /// elle-même (chargement paresseux) que par la liste (préchargement d'une page avant
        /// d'afficher les tuiles, pour que l'indicateur de chargement de la liste couvre toute
        /// l'attente).
        /// </summary>
        public async Task<ImageSource?> GetCoverAsync(Track track)
        {
            if (string.IsNullOrEmpty(track.FilePath))
                return null;

            if (_cache.TryGetValue(track.FilePath, out var cached))
                return cached;

            if (!string.IsNullOrEmpty(track.ImagePath) && File.Exists(track.ImagePath))
            {
                var fast = ImageSource.FromFile(track.ImagePath);
                _cache[track.FilePath] = fast;
                return fast;
            }

            await CoverLoadThrottle.WaitAsync();
            try
            {
                var bytes = await Task.Run(() => ExtractCoverThumbnailBytes(track.FilePath));
                if (bytes == null)
                {
                    _cache[track.FilePath] = null;
                    return null;
                }

                // Piste importée avant l'introduction de ImagePath : on persiste la pochette
                // maintenant qu'on l'a extraite, pour ne plus jamais avoir à reparser ce fichier.
                track.ImagePath = _fileService.SaveCoverThumbnail(bytes);
                _ = PersistImagePathAsync(track.Id, track.ImagePath);

                var image = ImageSource.FromStream(() => new MemoryStream(bytes));
                _cache[track.FilePath] = image;
                return image;
            }
            finally
            {
                CoverLoadThrottle.Release();
            }
        }

        /// <summary>
        /// Persistance en arrière-plan du chemin de vignette fraîchement extrait : l'affichage de
        /// la pochette n'attend pas l'écriture SQLite (effet de bord d'optimisation, auto-réparant
        /// — en cas d'échec, la pochette sera simplement ré-extraite au prochain affichage).
        /// L'exception est observée et tracée : un échec systématique (base verrouillée, disque
        /// plein...) resterait sinon invisible à jamais, avec ré-extraction et vignette orpheline
        /// à chaque affichage.
        /// </summary>
        private async Task PersistImagePathAsync(int trackId, string imagePath)
        {
            try
            {
                await _libraryDataService.UpdateTrackImagePathAsync(trackId, imagePath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CoverArtService] Échec de la persistance de la vignette (track {trackId}) : {ex}");
            }
        }

        /// <summary>
        /// Extrait la pochette embarquée d'un fichier audio et la redimensionne en petite vignette
        /// JPEG. Retourne null si le fichier n'a pas de pochette ou si l'extraction échoue.
        /// </summary>
        public static byte[]? ExtractCoverThumbnailBytes(string audioFilePath)
        {
            if (string.IsNullOrEmpty(audioFilePath))
                return null;

            try
            {
                // ReadStyle.None : seul tag.Pictures (les tags) est utilisé ici, jamais
                // file.Properties (durée, bitrate) - sans intérêt de payer le scan complet du flux
                // audio que TagLib fait par défaut (ReadStyle.Average) juste pour lire une pochette
                // embarquée. Chaque piste nouvellement importée (sans ImagePath en cache) paie ce
                // scan au premier affichage - déterminant pour la fluidité juste après un gros import.
                var file = TagLib.File.Create(audioFilePath, TagLib.ReadStyle.None);
                return ExtractCoverThumbnailBytes(file.Tag);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CoverArtService] Échec de lecture des tags/pochette ({audioFilePath}) : {ex}");
                return null;
            }
        }

        /// <summary>
        /// Variante réutilisant un TagLib.Tag déjà lu (évite de ré-ouvrir le fichier quand
        /// l'appelant a déjà parsé les tags pour le titre/la durée).
        /// </summary>
        public static byte[]? ExtractCoverThumbnailBytes(TagLib.Tag tag)
        {
            if (tag.Pictures.Length == 0)
                return null;

            var originalBytes = tag.Pictures[0].Data.Data;
            return CreateThumbnail(originalBytes) ?? originalBytes;
        }

        /// <summary>
        /// Redimensionne la pochette embarquée (souvent bien plus grande que nécessaire) en une petite
        /// vignette JPEG, pour éviter de garder des images pleine résolution en mémoire/sur disque pour
        /// un affichage de 120x120. Retourne null si le redimensionnement échoue (format non
        /// supporté...), auquel cas l'appelant garde l'image d'origine.
        /// </summary>
        private static byte[]? CreateThumbnail(byte[] originalBytes)
        {
            try
            {
                using var inputStream = new MemoryStream(originalBytes);
                using var image = PlatformImage.FromStream(inputStream);
                using var resized = image.Downsize(CoverThumbnailSize, disposeOriginal: false);
                using var outputStream = new MemoryStream();
                resized.Save(outputStream, ImageFormat.Jpeg, 0.85f);
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[CoverArtService] Échec du redimensionnement de la pochette : {ex}");
                return null;
            }
        }
    }
}
