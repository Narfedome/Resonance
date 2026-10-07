using CommunityToolkit.Maui.Storage;

namespace Resonance.Services
{
    public class FileService : ITrackFileStore
    {
        private readonly string _tracksDirectory;
        private readonly string _assetsDirectory;
        private readonly string _coversDirectory;

        public string TracksDirectory => _tracksDirectory;
        public string AssetsDirectory => _assetsDirectory;
        public string CoversDirectory => _coversDirectory;

        public FileService()
        {
            _assetsDirectory = Path.Combine(FileSystem.AppDataDirectory, "Assets");
            _tracksDirectory = Path.Combine(FileSystem.AppDataDirectory, "Tracks");
            _coversDirectory = Path.Combine(FileSystem.AppDataDirectory, "Covers");
            Directory.CreateDirectory(_assetsDirectory);
            Directory.CreateDirectory(_tracksDirectory);
            Directory.CreateDirectory(_coversDirectory);

            ClearPickerCache();
        }

        // Sur Android, FilePicker copie systématiquement le fichier choisi dans le cache privé de
        // l'appli (getCacheDir()) et ne le supprime jamais lui-même - comportement documenté et non
        // géré par MAUI. Sans ce coup de balai au démarrage, les copies laissées par les imports
        // précédant ce correctif restent coincées indéfiniment : invisibles pour le calcul de
        // stockage de l'appli (qui ne regarde que Tracks/Assets) mais comptées par Android dans la
        // taille totale de l'appli.
        // Vérifié via adb sur un appareil réel : ces copies n'atterrissent PAS à la racine du cache
        // mais dans des sous-dossiers imbriqués (cache/<hash>/<hash>/fichier.mp3, un niveau par
        // segment de l'URI content:// d'origine) - EnumerateFiles(dir) seul (non récursif) ne les
        // voit jamais, d'où un cache qui grossissait indéfiniment malgré ce nettoyage.
        private static void ClearPickerCache()
        {
            var dir = FileSystem.CacheDirectory;
            if (!Directory.Exists(dir))
                return;

            foreach (var entry in Directory.EnumerateFileSystemEntries(dir))
            {
                try
                {
                    if (Directory.Exists(entry))
                        Directory.Delete(entry, recursive: true);
                    else
                        File.Delete(entry);
                }
                catch { /* fichier ou dossier verrouillé, on continue */ }
            }
        }

        /// <summary>
        /// Supprime le fichier s'il s'agit d'une copie laissée par FilePicker dans le cache de l'appli
        /// (cf. ClearPickerCache) - ne touche jamais un fichier hors de ce dossier, pour ne pas risquer
        /// de supprimer le fichier original de l'utilisateur sur les plateformes où FilePicker renvoie
        /// directement son chemin réel (Windows, MacCatalyst) plutôt qu'une copie.
        /// </summary>
        public void DeleteIfCached(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !filePath.StartsWith(FileSystem.CacheDirectory, StringComparison.Ordinal))
                return;

            try { File.Delete(filePath); } catch { /* déjà supprimé ou verrouillé, sans conséquence */ }
        }

        public string CopyAssetToLocal(string originalFilePath)
        {
            if (string.IsNullOrEmpty(originalFilePath) || !File.Exists(originalFilePath))
                throw new FileNotFoundException(LocalizationService.Instance["ErrorSourceFileMissing"], originalFilePath);

            // Génère un nom unique pour éviter les collisions
            var destFileName = Guid.NewGuid().ToString() + Path.GetExtension(originalFilePath);
            var destPath = Path.Combine(_assetsDirectory, destFileName);

            File.Copy(originalFilePath, destPath, overwrite: true);
            return destPath;
        }

        /// <summary>
        /// Copie un fichier mp3 dans le dossier privé de l'application et retourne le nouveau path
        /// </summary>
        public string CopyTrackToLocal(string originalFilePath)
        {
            if (string.IsNullOrEmpty(originalFilePath) || !File.Exists(originalFilePath))
                throw new FileNotFoundException(LocalizationService.Instance["ErrorTrackFileMissing"], originalFilePath);

            // Génère un nom unique pour éviter les collisions
            var destFileName = Guid.NewGuid().ToString() + Path.GetExtension(originalFilePath);
            var destPath = Path.Combine(_tracksDirectory, destFileName);

            File.Copy(originalFilePath, destPath, overwrite: true);
            return destPath;
        }

        public string ReserveTrackPath(string extension)
        {
            var destFileName = Guid.NewGuid().ToString() + extension;
            return Path.Combine(_tracksDirectory, destFileName);
        }

        /// <summary>
        /// Sauvegarde une vignette de pochette (déjà extraite/redimensionnée) dans le dossier privé
        /// de l'application et retourne son chemin.
        /// </summary>
        public string SaveCoverThumbnail(byte[] jpegBytes)
        {
            var destPath = Path.Combine(_coversDirectory, Guid.NewGuid().ToString() + ".jpg");
            File.WriteAllBytes(destPath, jpegBytes);
            return destPath;
        }

        public void DeleteTrackFromLocal(string filePath)
        {
            // Suppression "best effort" : une track peut ne pas avoir de fichier associé (FilePath vide)
            // ou pointer vers un fichier déjà supprimé — dans ce cas il n'y a simplement rien à faire.
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
                return;

            // Un fichier encore verrouillé (player pas encore collecté sur Windows) ne doit pas
            // faire échouer toute une suppression en masse : il sera rattrapé par le nettoyage
            // des orphelins des réglages.
            try { File.Delete(filePath); } catch (IOException) { }
        }

        private static readonly FilePickerFileType AudioFileTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "public.audio" } },
            { DevicePlatform.Android, new[] { "audio/*" } },
            { DevicePlatform.WinUI, new[] { ".mp3", ".wav", ".m4a", ".opus" } },
            { DevicePlatform.MacCatalyst, new[] { "public.audio" } }
        });

        public async Task<FileResult?> PickAudioFileAsync(string pickerTitle)
        {
            return await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = pickerTitle,
                FileTypes = AudioFileTypes
            });
        }

        public async Task<IEnumerable<FileResult>?> PickAudioFilesAsync(string pickerTitle)
        {
            return await FilePicker.Default.PickMultipleAsync(new PickOptions
            {
                PickerTitle = pickerTitle,
                FileTypes = AudioFileTypes
            });
        }

        private static readonly FilePickerFileType DmPackFileTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
        {
            { DevicePlatform.iOS, new[] { "public.data" } },
            { DevicePlatform.Android, new[] { "application/octet-stream" } },
            { DevicePlatform.WinUI, new[] { ".dmpack" } },
            { DevicePlatform.MacCatalyst, new[] { "public.data" } }
        });

        public async Task<FileResult?> PickImportPackageAsync(string pickerTitle, IProgress<long>? copyProgress = null, CancellationToken cancellationToken = default)
        {
#if ANDROID
            return await PickImportPackageAndroidAsync(copyProgress, cancellationToken);
#else
            return await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = pickerTitle,
                FileTypes = DmPackFileTypes
            });
#endif
        }

#if ANDROID
        /// <summary>
        /// Contourne FilePicker.Default.PickAsync sur Android : pour un document content:// (Téléchargements,
        /// Drive...), celui-ci le recopie en interne dans le cache de l'appli via un Stream.CopyTo()
        /// synchrone exécuté sur le thread UI (cf. FileSystemUtils de .NET MAUI), avant même de rendre
        /// la main à notre code - pour un .dmpack de plusieurs Go, l'affichage reste figé (voire tronqué
        /// silencieusement) pendant toute la durée du transfert. Ici on récupère l'Uri content://
        /// directement (AndroidDmPackPicker, sans copie), puis on fait nous-mêmes la copie de façon
        /// asynchrone, avec suivi de progression et sans bloquer le thread UI.
        /// </summary>
        private async Task<FileResult?> PickImportPackageAndroidAsync(IProgress<long>? copyProgress, CancellationToken cancellationToken)
        {
            var uri = await Platforms.Android.AndroidDmPackPicker.PickAsync("application/octet-stream");
            if (uri == null)
                return null;

            var declaredSize = QueryDeclaredSize(uri);

            // Pendant l'import, la copie en cache ET les pistes déjà extraites vers le dossier Tracks
            // coexistent (rien ne supprime la copie avant la toute fin) - le pic d'espace nécessaire
            // avoisine donc le double de la taille du .dmpack. Vérifié avant de lancer une copie de
            // plusieurs Go pour rien : mieux vaut un message clair immédiat qu'un import qui échoue en
            // silence, piste par piste, une fois le disque plein en cours d'extraction.
            if (declaredSize.HasValue)
            {
                var available = GetAvailableSpace(FileSystem.CacheDirectory);
                var estimatedNeeded = declaredSize.Value * 2;
                if (available.HasValue && available.Value < estimatedNeeded)
                {
                    throw new IOException(string.Format(
                        LocalizationService.Instance["ErrorNotEnoughSpace"],
                        estimatedNeeded / (1024.0 * 1024.0 * 1024.0), available.Value / (1024.0 * 1024.0 * 1024.0)));
                }
            }

            var tempPath = Path.Combine(FileSystem.CacheDirectory, $"dmpack-import-{Guid.NewGuid():N}.dmpack");
            var completed = false;
            try
            {
                using var sourceStream = global::Android.App.Application.Context.ContentResolver?.OpenInputStream(uri)
                    ?? throw new IOException(LocalizationService.Instance["ErrorSourceFileMissing"]);
                long totalRead = 0;
                using (var destStream = File.Create(tempPath))
                {
                    // Buffer volontairement large (contre 80 Ko par défaut pour Stream.CopyToAsync) :
                    // réduit le nombre d'allers-retours IPC avec le fournisseur de contenu Android pour
                    // un fichier de plusieurs Go, sans complexifier la logique (toujours lecture puis
                    // écriture strictement séquentielles, pas de chevauchement).
                    var buffer = new byte[4 * 1024 * 1024];
                    int bytesRead;
                    while ((bytesRead = await sourceStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        await destStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                        totalRead += bytesRead;
                        copyProgress?.Report(totalRead);
                    }
                }

                // Sur un très gros fichier (plusieurs Go), la lecture du flux content:// s'est déjà
                // révélée s'arrêter en silence avant la fin (aucune exception, juste moins d'octets que
                // prévu) - sans doute une limite quelque part dans la chaîne ContentResolver côté
                // Android/fournisseur de contenu. Le zip lui-même reste alors valide (table des matières
                // lisible) mais amputé de sa fin, d'où des pistes rejetées une par une à l'import plutôt
                // qu'une erreur claire. On vérifie donc la taille copiée contre celle déclarée par Android
                // pour l'Uri source, pour transformer ce cas en erreur explicite plutôt qu'en import
                // silencieusement incomplet.
                if (declaredSize.HasValue && totalRead != declaredSize.Value)
                {
                    throw new IOException(string.Format(
                        LocalizationService.Instance["ErrorIncompleteFileCopy"],
                        totalRead / (1024.0 * 1024.0), declaredSize.Value / (1024.0 * 1024.0)));
                }

                completed = true;
                return new FileResult(tempPath);
            }
            finally
            {
                // Copie interrompue (annulation, erreur) : le fichier partiel ne serait sinon jamais
                // nettoyé avant le prochain démarrage de l'appli (cf. ClearPickerCache).
                if (!completed)
                {
                    try { File.Delete(tempPath); } catch { /* meilleur effort */ }
                }
            }
        }

        private static long? QueryDeclaredSize(global::Android.Net.Uri uri)
        {
            try
            {
                using var cursor = global::Android.App.Application.Context.ContentResolver?.Query(
                    uri, new[] { global::Android.Provider.IOpenableColumns.Size }, null, null, null);
                if (cursor == null || !cursor.MoveToFirst())
                    return null;

                var index = cursor.GetColumnIndex(global::Android.Provider.IOpenableColumns.Size);
                return index != -1 && !cursor.IsNull(index) ? cursor.GetLong(index) : null;
            }
            catch
            {
                // La taille déclarée n'est qu'une vérification en plus, pas une exigence : certains
                // fournisseurs de contenu ne la renseignent pas (ou la requête échoue) sans que ce soit
                // en soi un problème pour l'import.
                return null;
            }
        }

        private static long? GetAvailableSpace(string path)
        {
            try
            {
                using var file = new global::Java.IO.File(path);
                return file.UsableSpace;
            }
            catch
            {
                // Meilleur effort : si l'espace disponible ne peut pas être déterminé, on laisse l'import
                // se lancer normalement plutôt que de bloquer sur une vérification qui a échoué.
                return null;
            }
        }
#endif

        /// <summary>
        /// Ouvre la boîte de dialogue "Enregistrer sous" de la plateforme et y écrit le flux fourni.
        /// Retourne le chemin choisi par l'utilisateur, ou null si l'enregistrement a été annulé.
        /// </summary>
        public async Task<string?> SaveExportPackageAsync(string fileName, Stream stream, CancellationToken cancellationToken)
        {
            var result = await FileSaver.Default.SaveAsync(fileName, stream, cancellationToken);
            return result.IsSuccessful ? result.FilePath : null;
        }
    }
}
