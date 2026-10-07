using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Resonance.Models;
using Resonance.Models.ImportExport;
using Resonance.Models.Library;

namespace Resonance.Services
{
    /// <summary>
    /// Export/import de campagnes et de la bibliothèque au format .dmpack (zip renommé, cf.
    /// documentation du format). Ne dépend que des services de données existants (ISceneDataService,
    /// ILibraryDataService) — aucun accès SQL direct : l'import n'écrit jamais par-dessus une ligne
    /// existante, il insère toujours de nouvelles lignes via les mêmes Save*Async que le reste de
    /// l'appli utilise, avec dédup des tracks par hash (FindTrackByHashAsync).
    ///
    /// Le zip lu à l'import est traité comme non fiable (peut venir d'un tiers) : toute entrée dont le
    /// nom ne correspond pas exactement au format attendu ("manifest.json", "manifest.sig" ou
    /// "tracks/&lt;hash SHA256&gt;.&lt;ext&gt;") fait rejeter l'archive entière avant la moindre
    /// extraction, ce qui élimine tout risque de zip-slip. La taille déclarée de chaque entrée et le
    /// total sont plafonnés pour se prémunir d'un zip-bomb, et le hash de chaque fichier extrait est
    /// revérifié avant d'être accepté en bibliothèque.
    ///
    /// Le manifeste est signé (HMAC-SHA256, clé fixe embarquée dans l'appli) : toute édition du
    /// manifest.json après export (titres, volumes, structure de campagne...), par exemple via un
    /// logiciel de zip, invalide la signature et fait rejeter l'archive à l'import. Ça détecte aussi
    /// une corruption accidentelle du fichier. Ce n'est en revanche pas une protection contre un
    /// attaquant qui décompile l'appli pour en extraire la clé et forger une nouvelle signature — ce
    /// n'est pas l'objectif ici, seulement d'empêcher une modification "à la main" silencieuse.
    /// </summary>
    public class ImportExportService : IImportExportService
    {
        private const int FormatVersion = 1;
        // Garde-fous anti zip-bomb (cf. doc de classe), pas des limites d'usage normal : une piste d'1h
        // en Opus tient dans ~60 Mo, donc 500 Mo par piste laisse une large marge. 20 Go au total couvre
        // une bibliothèque complète (FullBackup) sans jamais s'en approcher pour un usage légitime.
        private const long MaxTrackEntryBytes = 500L * 1024 * 1024;
        private const long MaxTotalUncompressedBytes = 20L * 1024 * 1024 * 1024;
        private const string ManifestEntryName = "manifest.json";
        private const string SignatureEntryName = "manifest.sig";

        private static readonly byte[] ManifestSignatureKey =
            Convert.FromHexString("61d46ee3f647bca5824870dc1330e22e9f5201b2b395c17cba19f27e790ef21e");

        private static readonly Regex TrackEntryPattern = new(@"^tracks/[a-fA-F0-9]{64}\.[a-z0-9]{1,5}$", RegexOptions.Compiled);
        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = false };

        private readonly ISceneDataService _sceneDataService;
        private readonly ILibraryDataService _libraryDataService;
        private readonly ITrackFileStore _trackFileStore;

        public ImportExportService(ISceneDataService sceneDataService, ILibraryDataService libraryDataService, ITrackFileStore trackFileStore)
        {
            _sceneDataService = sceneDataService;
            _libraryDataService = libraryDataService;
            _trackFileStore = trackFileStore;
        }

        // ── Export ────────────────────────────────────────────────

        public async Task ExportAsync(ExportRequest request, Stream destination, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            var manifest = new ExportManifest
            {
                FormatVersion = FormatVersion,
                ExportLevel = (int)request.Level,
                ExportedAt = DateTime.UtcNow
            };

            var trackIdsToInclude = new HashSet<int>();

            switch (request.Level)
            {
                case ExportLevel.StructureOnly:
                case ExportLevel.StructureWithChannels:
                    var campaign = (await _sceneDataService.GetCampaignsAsync())
                        .FirstOrDefault(c => c.Id == request.CampaignId)
                        ?? throw new InvalidOperationException($"Campagne {request.CampaignId} introuvable.");
                    manifest.Campaigns.Add(await BuildCampaignExportAsync(
                        campaign, includeChannels: request.Level == ExportLevel.StructureWithChannels, trackIdsToInclude, cancellationToken));
                    break;

                case ExportLevel.AudioLibraryOnly:
                    foreach (var t in (await _libraryDataService.GetAllItemsTypeAsync(typeof(Track))).OfType<Track>())
                        trackIdsToInclude.Add(t.Id);
                    break;

                case ExportLevel.FullBackup:
                    foreach (var c in await _sceneDataService.GetCampaignsAsync())
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        manifest.Campaigns.Add(await BuildCampaignExportAsync(c, includeChannels: true, trackIdsToInclude, cancellationToken));
                    }
                    foreach (var t in (await _libraryDataService.GetAllItemsTypeAsync(typeof(Track))).OfType<Track>())
                        trackIdsToInclude.Add(t.Id);

                    manifest.Library = await BuildLibraryExportAsync();
                    break;
            }

            using var zip = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true);

            var allTracksById = (await _libraryDataService.GetAllItemsTypeAsync(typeof(Track)))
                .OfType<Track>()
                .Where(t => trackIdsToInclude.Contains(t.Id))
                .ToDictionary(t => t.Id);

            int processed = 0;

            // Trié par Id (ordre de création initial dans la bibliothèque), pas par ordre d'itération
            // du HashSet (qui suit l'ordre de traversée des scènes d'une campagne) : la liste "toute la
            // bibliothèque" sort ainsi toujours dans son ordre d'origine, indépendamment des campagnes
            // qui référencent chaque piste - un réimport reproduit alors le même ordre relatif que la
            // bibliothèque source, les liens de campagne n'étant que des références par Id dans le
            // manifeste (cf. BuildCampaignExportAsync, déjà construit avant cette boucle).
            foreach (var trackId in trackIdsToInclude.OrderBy(id => id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!allTracksById.TryGetValue(trackId, out var track) || string.IsNullOrEmpty(track.FilePath) || !File.Exists(track.FilePath))
                    continue;

                var hash = string.IsNullOrEmpty(track.Hash) ? TrackTagHelper.ComputeSha256(track.FilePath) : track.Hash;
                var ext = Path.GetExtension(track.FilePath).ToLowerInvariant();
                var entryName = $"tracks/{hash}{ext}";

                manifest.Tracks.Add(new TrackExport
                {
                    Id = track.Id,
                    Title = track.Title,
                    Category = track.Category,
                    DurationSeconds = track.Duration.TotalSeconds,
                    DefaultVolume = track.Volume,
                    Hash = hash,
                    FileEntry = entryName
                });

                var entry = zip.CreateEntry(entryName, CompressionLevel.NoCompression);
                using (var entryStream = entry.Open())
                using (var fileStream = File.OpenRead(track.FilePath))
                    await fileStream.CopyToAsync(entryStream, cancellationToken);

                progress?.Report(new ExportProgress { CurrentItem = track.Title, Processed = ++processed, Total = allTracksById.Count });
            }

            var manifestBytes = JsonSerializer.SerializeToUtf8Bytes(manifest, JsonOptions);

            var manifestEntry = zip.CreateEntry(ManifestEntryName, CompressionLevel.Optimal);
            using (var manifestStream = manifestEntry.Open())
                await manifestStream.WriteAsync(manifestBytes, cancellationToken);

            var signatureEntry = zip.CreateEntry(SignatureEntryName, CompressionLevel.Optimal);
            using (var signatureStream = signatureEntry.Open())
            {
                var signatureHex = Encoding.ASCII.GetBytes(Convert.ToHexString(ComputeManifestSignature(manifestBytes)));
                await signatureStream.WriteAsync(signatureHex, cancellationToken);
            }
        }

        /// <summary>Internal (pas private) uniquement pour permettre aux tests de signer un manifeste
        /// forgé à la main lorsqu'ils veulent isoler une autre cause de rejet.</summary>
        internal static byte[] ComputeManifestSignature(byte[] manifestBytes)
        {
            using var hmac = new HMACSHA256(ManifestSignatureKey);
            return hmac.ComputeHash(manifestBytes);
        }

        private async Task<CampaignExport> BuildCampaignExportAsync(Campaign campaign, bool includeChannels, HashSet<int> trackIdsToInclude, CancellationToken cancellationToken)
        {
            var campaignExport = new CampaignExport { Id = campaign.Id, Title = campaign.Title };

            foreach (var session in await _sceneDataService.GetSessionsAsync(campaign.Id))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var sessionExport = new SessionExport { Id = session.Id, Title = session.Title };

                foreach (var scene in await _sceneDataService.GetScenesAsync(session.Id))
                {
                    var sceneExport = new SceneExport { Id = scene.Id, Title = scene.Title };

                    if (includeChannels)
                    {
                        foreach (var sceneTrack in await _sceneDataService.GetSceneTracksAsync(scene.Id))
                        {
                            trackIdsToInclude.Add(sceneTrack.Track.Id);
                            sceneExport.SceneTracks.Add(new SceneTrackExport
                            {
                                TrackId = sceneTrack.Track.Id,
                                Volume = sceneTrack.Volume,
                                Position = sceneTrack.Position,
                                AutoPlay = sceneTrack.AutoPlay,
                                IsLooping = sceneTrack.IsLooping,
                                FadeIn = sceneTrack.FadeIn,
                                FadeOut = sceneTrack.FadeOut
                            });
                        }
                    }

                    sessionExport.Scenes.Add(sceneExport);
                }

                campaignExport.Sessions.Add(sessionExport);
            }

            return campaignExport;
        }

        private async Task<LibraryExport> BuildLibraryExportAsync()
        {
            var library = new LibraryExport();

            foreach (var s in (await _libraryDataService.GetAllItemsTypeAsync(typeof(Spell))).OfType<Spell>())
                library.Spells.Add(new SpellExport { Id = s.Id, Title = s.Title, Description = s.Description });

            foreach (var name in await _libraryDataService.GetCategoryNamesAsync(typeof(Track)))
                library.Categories.Add(new CategoryExport { Name = name, LibraryType = nameof(Track) });
            foreach (var name in await _libraryDataService.GetCategoryNamesAsync(typeof(Spell)))
                library.Categories.Add(new CategoryExport { Name = name, LibraryType = nameof(Spell) });

            return library;
        }

        // ── Import ────────────────────────────────────────────────
        //
        // Historique et raisons des choix ci-dessous (mécanique non triviale, à ne pas re-régresser
        // sans relire ce qui suit) :
        //
        // Version initiale (avant optimisation) : chaque piste était extraite vers un fichier
        // temporaire, son hash recalculé en relisant ce fichier en entier, sa décodabilité vérifiée en
        // le relisant une troisième fois (TagLib), puis copiée une seconde fois vers son emplacement
        // définitif - soit ~2 écritures et ~3 lectures complètes par piste pour une seule opération
        // logiquement nécessaire. Mesuré sur une bibliothèque réelle de 239 pistes (~9 Go) : environ
        // 12 minutes rien que pour l'extraction+hash+décodabilité.
        //
        // Ce qui a été changé, et pourquoi :
        //  1) Écriture directe à l'emplacement définitif (ExtractAndVerifyHashAsync), hash calculé à la
        //     volée pendant cette même écriture (IncrementalHash) au lieu d'une passe de lecture dédiée
        //     après coup - élimine la moitié des passes disque.
        //  2) Buffer de copie remonté de 80 Ko (défaut Stream.CopyToAsync) à 4 Mo - a divisé par ~3 le
        //     temps de cette phase à lui seul (mesuré : 455s -> 151s sur la même bibliothèque), en
        //     réduisant le nombre d'allers-retours de lecture/écriture.
        //  3) IsDecodableAudio bascule sur TagLib.ReadStyle.None : la détection de format (ce qui nous
        //     intéresse ici) se fait indépendamment du ReadStyle chez TagLib, seul le calcul du bitrate
        //     moyen (coûteux, un scan complet du flux audio) est sauté. Le check historique exigeait en
        //     plus Duration > 0 pour rejeter un conteneur reconnu mais vide - abandonné ici (Duration
        //     vaut toujours zéro sous ReadStyle.None) car : (a) Track.Duration à l'import vient déjà du
        //     manifeste, jamais de ce scan ; (b) IsDecodableAudio n'est utilisé que par l'import (seul
        //     appelant, vérifié) donc rien d'autre n'en dépend ; (c) le hash SHA256 déjà vérifié avant
        //     cet appel garantit que le contenu correspond exactement au fichier original exporté - un
        //     faux fichier ne le passerait pas. Gain mesuré : 283s -> 222s (moins que "quasiment zéro"
        //     espéré : pour l'Ogg/Opus, la lecture des pages d'en-tête de flux se fait elle aussi
        //     indépendamment du ReadStyle et représente une part significative du coût, incompressible
        //     sans réécrire nous-mêmes le parsing Ogg).
        //  4) Pipeline en 3 phases plutôt qu'un seul passage séquentiel par piste (cf.
        //     ImportTracksAsync) : la phase 2 (décodabilité, le poste de temps restant le plus lourd)
        //     est parallélisée entre plusieurs pistes à la fois, chaque piste étant déjà écrite sur son
        //     propre fichier local à ce stade - indépendant du ZipArchive partagé, donc sans risque. La
        //     phase 1 (extraction depuis le zip), elle, DOIT rester séquentielle : ZipArchive ne permet
        //     pas la lecture concurrente de plusieurs entrées sur la même instance/flux partagé (vérifié
        //     dans la doc .NET avant d'implémenter - piste explicitement écartée pour cette étape).
        //
        // Limite rencontrée, importante à connaître avant de retoucher ce fichier : le gain réel de la
        // parallélisation de la phase 2 dépend fortement de la plateforme. Mesuré sur un émulateur
        // Android : 222s séquentiel -> 193s réel en parallèle à 4 (~13% seulement, pas les ~75%
        // attendus d'un vrai parallélisme CPU) - le stockage flash mobile ne scale pas bien avec des
        // lectures concurrentes, le disque reste le facteur limitant, pas le CPU. Sur Windows (SSD) en
        // revanche : 42s d'extraction contre 37s de décodabilité, un ratio bien plus proche de l'idéal -
        // la parallélisation y est un vrai gain net. Non vérifié : un appareil Android réel (pas
        // l'émulateur, dont le disque virtuel QCOW2 ajoute un overhead que le stockage flash natif d'un
        // téléphone n'a pas) pourrait très bien scaler mieux que ce qui a été mesuré ici.
        //
        // Alternatives de librairies envisagées puis écartées (aucune n'aurait aidé) : CryptoStream à la
        // place de la boucle read+hash+write manuelle (même travail réel, perte du contrôle sur la
        // taille de buffer) ; une lib zip tierce (nos entrées sont stockées sans compression à l'export,
        // "extraire" est déjà une simple copie d'octets) ; un sniffer de format plus léger que TagLib
        // (affaiblirait la vraie validation de structure contre un fichier forgé, pour gagner quelques
        // secondes).

        public async Task<ImportResult> ImportAsync(Stream source, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default)
        {
            using var zip = new ZipArchive(source, ZipArchiveMode.Read, leaveOpen: true);

            ValidateArchiveEntries(zip);

            var manifestEntry = zip.GetEntry(ManifestEntryName)
                ?? throw new InvalidDataException("Fichier .dmpack invalide : manifeste manquant.");
            var signatureEntry = zip.GetEntry(SignatureEntryName)
                ?? throw new InvalidDataException("Fichier .dmpack invalide : signature manquante.");

            byte[] manifestBytes;
            using (var manifestStream = manifestEntry.Open())
            using (var buffer = new MemoryStream())
            {
                await manifestStream.CopyToAsync(buffer, cancellationToken);
                manifestBytes = buffer.ToArray();
            }

            string declaredSignatureHex;
            using (var signatureStream = signatureEntry.Open())
            using (var reader = new StreamReader(signatureStream, Encoding.ASCII))
                declaredSignatureHex = (await reader.ReadToEndAsync(cancellationToken)).Trim();

            byte[] declaredSignature;
            try
            {
                declaredSignature = Convert.FromHexString(declaredSignatureHex);
            }
            catch (FormatException)
            {
                throw new InvalidDataException("Fichier .dmpack invalide : signature illisible.");
            }

            if (!CryptographicOperations.FixedTimeEquals(ComputeManifestSignature(manifestBytes), declaredSignature))
                throw new InvalidDataException("Archive .dmpack corrompue ou modifiée : signature invalide.");

            var manifest = JsonSerializer.Deserialize<ExportManifest>(manifestBytes, JsonOptions)
                ?? throw new InvalidDataException("Fichier .dmpack invalide : manifeste illisible.");
            ValidateManifest(manifest);

            var result = new ImportResult();
            var trackIdMap = new Dictionary<int, int>();

            // Ordre volontaire en 3 temps plutôt qu'un traitement des campagnes interfolé avec les
            // pistes : 1) toute la bibliothèque (pistes, sorts, catégories) d'abord, aucune campagne
            // n'en dépend jamais dans l'autre sens ; 2) la structure Campagne/Chapitre/Scène, sans
            // aucune référence à une piste ; 3) le lien scène-piste (SceneTrack) en dernier, une fois
            // que les deux existent des deux côtés (trackIdMap complet, scènes créées).
            await ImportTracksAsync(zip, manifest.Tracks, result, trackIdMap, progress, cancellationToken);

            if (manifest.Library != null)
            {
                foreach (var spell in manifest.Library.Spells)
                {
                    await _libraryDataService.SaveLibraryItemAsync(new Spell { Title = spell.Title, Description = spell.Description });
                    result.SpellsImported++;
                }

                foreach (var category in manifest.Library.Categories)
                {
                    var type = category.LibraryType == nameof(Spell) ? typeof(Spell) : typeof(Track);
                    await _libraryDataService.EnsureCategoryAsync(type, category.Name);
                }
            }

            // (SceneExport importé, Id de la scène nouvellement créée) : accumulé pendant la création
            // de la structure, consommé juste après pour créer les SceneTrack - la scène "de destination"
            // n'existe qu'une fois la structure entière posée, donc ce lien ne peut pas se faire dans la
            // même passe sans risquer de référencer une scène pas encore enregistrée.
            var sceneLinks = new List<(SceneExport SceneExport, int NewSceneId)>();
            foreach (var campaignExport in manifest.Campaigns)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ImportCampaignStructureAsync(campaignExport, result, sceneLinks);
            }

            foreach (var (sceneExport, newSceneId) in sceneLinks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await ImportSceneTracksAsync(sceneExport, newSceneId, trackIdMap);
            }

            return result;
        }

        /// <summary>
        /// Rejette l'archive entière si une seule entrée ne correspond pas au format attendu (jamais
        /// de chemin de traversée possible : le nom validé sert ensuite tel quel à l'extraction) ou si
        /// la taille déclarée totale dépasse le plafond anti zip-bomb.
        /// </summary>
        private static void ValidateArchiveEntries(ZipArchive zip)
        {
            long totalDeclared = 0;
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName != ManifestEntryName && entry.FullName != SignatureEntryName && !TrackEntryPattern.IsMatch(entry.FullName))
                    throw new InvalidDataException($"Entrée d'archive non reconnue : {entry.FullName}");

                if (entry.Length > MaxTrackEntryBytes)
                    throw new InvalidDataException($"Entrée d'archive trop volumineuse : {entry.FullName}");

                totalDeclared += entry.Length;
            }

            if (totalDeclared > MaxTotalUncompressedBytes)
                throw new InvalidDataException("Archive .dmpack trop volumineuse.");
        }

        private static void ValidateManifest(ExportManifest manifest)
        {
            if (manifest.FormatVersion != FormatVersion)
                throw new InvalidDataException($"Version de format .dmpack non supportée : {manifest.FormatVersion}.");

            if (manifest.ExportLevel is < 1 or > 4)
                throw new InvalidDataException("Niveau d'export invalide dans le manifeste.");

            foreach (var track in manifest.Tracks)
            {
                if (string.IsNullOrWhiteSpace(track.Hash) || track.Hash.Length != 64)
                    throw new InvalidDataException("Hash de track invalide dans le manifeste.");

                if (!TrackEntryPattern.IsMatch(track.FileEntry) ||
                    !track.FileEntry.StartsWith($"tracks/{track.Hash}.", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Référence de fichier incohérente avec le hash déclaré.");
            }
        }

        /// <summary>
        /// Traite les pistes en 3 phases plutôt qu'une par une de bout en bout :
        /// 1) Extraction + hash - séquentielle (ZipArchive n'autorise pas de lecture concurrente de
        ///    plusieurs entrées sur la même instance/flux partagé - cf. investigation qui a écarté la
        ///    parallélisation de cette étape précise).
        /// 2) Vérification de décodabilité (TagLib) - chaque piste est déjà écrite sur son propre
        ///    fichier local à ce stade, indépendant du zip partagé : plusieurs peuvent être validées
        ///    en parallèle sans aucun risque de corruption, ce qui divise son temps total par le degré
        ///    de parallélisme choisi (mesuré comme le deuxième poste de temps le plus important après
        ///    l'extraction, cf. ImportResult.VerificationDuration).
        /// 3) Sauvegarde en base - séquentielle (pas de gain à paralléliser des écritures SQLite, déjà
        ///    mesuré comme négligeable, cf. ImportResult.DatabaseSaveDuration).
        /// </summary>
        private async Task ImportTracksAsync(ZipArchive zip, List<TrackExport> tracks, ImportResult result,
            Dictionary<int, int> trackIdMap, IProgress<ImportProgress>? progress, CancellationToken cancellationToken)
        {
            // Temps réel total des 3 phases, affiché à l'utilisateur en fin d'import (cf.
            // ImportResult.TotalDuration) - mesuré séparément de la somme des durées par phase, qui
            // peut être trompeuse une fois qu'une phase se parallélise (cf. phase 2 ci-dessous).
            var totalStopwatch = Stopwatch.StartNew();

            var total = tracks.Count;
            var processed = 0;

            var pending = new List<(TrackExport TrackExport, string LocalPath)>();
            // Réutilisé entre pistes plutôt que réalloué à chaque appel : évite de faire churner un
            // buffer de 4 Mo par piste (pression GC inutile sur une bibliothèque de plusieurs centaines
            // de pistes). Sûr ici : phase 1 reste strictement séquentielle.
            var buffer = new byte[4 * 1024 * 1024];

            // Couvre les 3 phases, pas seulement 2 et 3 : une annulation ou une erreur pendant la phase
            // 1 elle-même (extraction, la plus longue - cf. doc de classe) laisserait sinon les pistes
            // déjà extraites avant l'interruption orphelines sur disque, jamais nettoyées (le filet plus
            // bas ne s'exécutait alors jamais, resté hors de portée de cette boucle).
            var resolvedPaths = new HashSet<string>();
            try
            {
                foreach (var trackExport in tracks)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var localPath = await ExtractAndVerifyHashAsync(zip, trackExport, result, trackIdMap, buffer, cancellationToken);
                    if (localPath != null)
                        pending.Add((trackExport, localPath));

                    progress?.Report(new ImportProgress { CurrentItem = trackExport.Title, Processed = ++processed, Total = total });
                }

                // Tableau indexé (pas un ConcurrentBag) : chaque tâche parallèle écrit à SA position
                // d'origine plutôt que d'ajouter au fil de l'eau - un ConcurrentBag ne garantit aucun
                // ordre d'énumération, ce qui mélangeait l'ordre des pistes à la sauvegarde (phase 3,
                // séquentielle) selon le hasard de la concurrence. Comme l'affichage de la bibliothèque
                // trie par Id (donc par ordre d'insertion), l'ordre du manifeste d'origine se perdait.
                var outcomes = new (TrackExport TrackExport, string LocalPath, bool IsDecodable)[pending.Count];

                // Chronomètre la phase dans son ensemble (temps réel écoulé), pas la somme des durées
                // de chaque tâche individuelle - avec plusieurs tâches concurrentes, cette somme n'a
                // plus aucun rapport avec le temps réellement écoulé.
                var phaseStopwatch = Stopwatch.StartNew();
                var verifiedCount = 0;
                await Parallel.ForEachAsync(Enumerable.Range(0, pending.Count),
                    new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = cancellationToken },
                    async (i, ct) =>
                    {
                        var item = pending[i];
                        bool isDecodable;
                        try
                        {
                            isDecodable = await Task.Run(() => TrackTagHelper.IsDecodableAudio(item.LocalPath), ct);
                        }
                        catch (OperationCanceledException)
                        {
                            throw;
                        }
                        catch
                        {
                            // Lecture du fichier fraîchement écrit en échec (disque, permissions...) :
                            // traité comme non décodable plutôt que de faire échouer tout le lot pour
                            // cette seule piste.
                            isDecodable = false;
                        }
                        outcomes[i] = (item.TrackExport, item.LocalPath, isDecodable);

                        // Compteur dédié à cette phase (pas le Processed/Total de la phase 1, déjà à
                        // son maximum) : IsVerifyingTracks signale à l'appelant de changer de message
                        // affiché plutôt que de réutiliser le décompte de pistes de la phase 1.
                        progress?.Report(new ImportProgress
                        {
                            Processed = Interlocked.Increment(ref verifiedCount),
                            Total = pending.Count,
                            IsVerifyingTracks = true
                        });
                    });
                result.VerificationDuration = phaseStopwatch.Elapsed;

                foreach (var outcome in outcomes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!outcome.IsDecodable)
                    {
                        result.TracksRejectedNotDecodable++;
                        result.TracksRejected++;
                        resolvedPaths.Add(outcome.LocalPath);
                        try { File.Delete(outcome.LocalPath); } catch { /* meilleur effort */ }
                        continue;
                    }

                    try
                    {
                        var track = new Track
                        {
                            Title = outcome.TrackExport.Title,
                            Category = outcome.TrackExport.Category,
                            Duration = TimeSpan.FromSeconds(outcome.TrackExport.DurationSeconds),
                            Volume = outcome.TrackExport.DefaultVolume,
                            Hash = outcome.TrackExport.Hash,
                            FilePath = outcome.LocalPath
                        };

                        var dbStopwatch = Stopwatch.StartNew();
                        await _libraryDataService.SaveLibraryItemAsync(track);

                        // La catégorie du manifeste n'est autrement enregistrée que pour un backup complet
                        // (via manifest.Library.Categories) : pour les autres niveaux d'export, une catégorie
                        // "custom" (ni Musique/Ambiance/SFX) resterait un simple texte sur la track, invisible
                        // dans le sélecteur/la gestion des catégories tant qu'elle n'existe pas ici aussi.
                        if (!string.IsNullOrEmpty(track.Category))
                            await _libraryDataService.EnsureCategoryAsync(typeof(Track), track.Category);
                        result.DatabaseSaveDuration += dbStopwatch.Elapsed;

                        result.TracksCopied++;
                        result.ImportedTracks.Add(track);
                        trackIdMap[outcome.TrackExport.Id] = track.Id;
                        resolvedPaths.Add(outcome.LocalPath);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch
                    {
                        // La sauvegarde en base peut échouer (disque plein...) une fois la piste déjà
                        // écrite et validée : rejetée simplement plutôt que de faire échouer tout
                        // l'import à cause d'elle.
                        result.TracksRejectedOther++;
                        result.TracksRejected++;
                        resolvedPaths.Add(outcome.LocalPath);
                        try { File.Delete(outcome.LocalPath); } catch { /* meilleur effort */ }
                    }
                }
            }
            finally
            {
                // Une piste en attente jamais résolue (annulation ou autre exception ayant coupé le
                // traitement en plein milieu, phase 1 comprise depuis que le try l'englobe aussi)
                // resterait orpheline sur disque sans ce filet.
                foreach (var item in pending)
                {
                    if (!resolvedPaths.Contains(item.LocalPath))
                    {
                        try { File.Delete(item.LocalPath); } catch { /* meilleur effort */ }
                    }
                }
            }

            result.TotalDuration = totalStopwatch.Elapsed;
        }

        /// <summary>
        /// Phase 1 (dédup + extraction + vérification de hash) pour une piste. Renvoie le chemin local
        /// si la piste doit être validée en phase 2 (décodabilité), ou null si déjà résolue ici
        /// (réutilisée, entrée manquante, ou hash invalide - result/trackIdMap déjà mis à jour dans ce
        /// cas).
        /// </summary>
        private async Task<string?> ExtractAndVerifyHashAsync(ZipArchive zip, TrackExport trackExport, ImportResult result,
            Dictionary<int, int> trackIdMap, byte[] buffer, CancellationToken cancellationToken)
        {
            var existing = await _libraryDataService.FindTrackByHashAsync(trackExport.Hash, excludeId: 0);
            if (existing != null)
            {
                result.TracksReused++;
                trackIdMap[trackExport.Id] = existing.Id;
                return null;
            }

            var entry = zip.GetEntry(trackExport.FileEntry);
            if (entry == null)
            {
                result.TracksRejectedMissingEntry++;
                result.TracksRejected++;
                return null;
            }

            // Écrit directement à l'emplacement définitif (dossier Tracks) en calculant le hash à la
            // volée pendant la copie, plutôt que d'extraire vers un fichier temporaire puis de le
            // recopier une seconde fois vers le stockage local (cf. l'ancien appel à CopyTrackToLocal) :
            // pour une bibliothèque de plusieurs Go, ça évite de relire/réécrire chaque piste deux fois.
            var localPath = _trackFileStore.ReserveTrackPath(Path.GetExtension(trackExport.FileEntry));
            var stopwatch = Stopwatch.StartNew();
            try
            {
                using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
                using (var entryStream = entry.Open())
                using (var fileStream = File.Create(localPath))
                {
                    int bytesRead;
                    while ((bytesRead = await entryStream.ReadAsync(buffer, cancellationToken)) > 0)
                    {
                        hasher.AppendData(buffer, 0, bytesRead);
                        await fileStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
                    }
                }
                var actualHash = Convert.ToHexString(hasher.GetHashAndReset());
                result.ExtractionDuration += stopwatch.Elapsed;

                if (!string.Equals(actualHash, trackExport.Hash, StringComparison.OrdinalIgnoreCase))
                {
                    result.TracksRejectedHashMismatch++;
                    result.TracksRejected++;
                    try { File.Delete(localPath); } catch { /* meilleur effort */ }
                    return null;
                }

                return localPath;
            }
            catch (OperationCanceledException)
            {
                try { File.Delete(localPath); } catch { /* meilleur effort */ }
                throw;
            }
            catch
            {
                // L'écriture peut échouer (disque plein...) une fois la piste déjà partiellement
                // écrite : rejetée simplement plutôt que de faire échouer tout l'import à cause d'elle.
                result.TracksRejectedOther++;
                result.TracksRejected++;
                try { File.Delete(localPath); } catch { /* meilleur effort */ }
                return null;
            }
        }

        // Pas de transaction englobante ici, contrairement aux suppressions en cascade de
        // SceneDataService : un import interrompu (annulation utilisateur via CancellationToken, ou
        // crash) laisse la campagne partiellement importee plutot que de tout annuler, coherent avec
        // le reste de ce fichier (ImportTrackAsync rejette une piste individuelle plutot que de faire
        // echouer tout l'import). Cout : une campagne incomplete en base si interrompu en cours de
        // route (pas de perte d'integrite referentielle - chaque ligne enfant reference bien un
        // parent reellement sauvegarde, juste incomplet). Choix delibere, pas un oubli.
        /// <summary>
        /// Crée la structure Campagne/Chapitre/Scène, sans aucun lien vers une piste (cf.
        /// ImportSceneTracksAsync, appelée séparément une fois toute la structure posée).
        /// </summary>
        private async Task ImportCampaignStructureAsync(CampaignExport campaignExport, ImportResult result, List<(SceneExport SceneExport, int NewSceneId)> sceneLinks)
        {
            var campaign = new Campaign { Title = campaignExport.Title };
            await _sceneDataService.SaveCampaignAsync(campaign);
            result.CampaignsImported++;

            foreach (var sessionExport in campaignExport.Sessions)
            {
                var session = new Session { CampaignId = campaign.Id, Title = sessionExport.Title };
                await _sceneDataService.SaveSessionAsync(session);

                foreach (var sceneExport in sessionExport.Scenes)
                {
                    var scene = new Scene { SessionId = session.Id, Title = sceneExport.Title };
                    await _sceneDataService.SaveSceneAsync(scene);
                    sceneLinks.Add((sceneExport, scene.Id));
                }
            }
        }

        /// <summary>
        /// Crée les SceneTrack (lien scène-piste) d'une scène déjà créée par
        /// ImportCampaignStructureAsync, une fois la bibliothèque de pistes entièrement importée.
        /// </summary>
        private async Task ImportSceneTracksAsync(SceneExport sceneExport, int sceneId, Dictionary<int, int> trackIdMap)
        {
            foreach (var sceneTrackExport in sceneExport.SceneTracks)
            {
                // Track absente du manifeste ou rejetée à l'import (sécurité) : le canal est
                // simplement omis plutôt que de faire échouer tout l'import de la campagne.
                if (!trackIdMap.TryGetValue(sceneTrackExport.TrackId, out var newTrackId))
                    continue;

                await _sceneDataService.SaveSceneTrackAsync(new SceneTrack
                {
                    SceneId = sceneId,
                    Track = new Track { Id = newTrackId },
                    Position = sceneTrackExport.Position,
                    Volume = sceneTrackExport.Volume,
                    AutoPlay = sceneTrackExport.AutoPlay,
                    IsLooping = sceneTrackExport.IsLooping,
                    FadeIn = sceneTrackExport.FadeIn,
                    FadeOut = sceneTrackExport.FadeOut
                });
            }
        }
    }

    public interface IImportExportService
    {
        Task ExportAsync(ExportRequest request, Stream destination, IProgress<ExportProgress>? progress = null, CancellationToken cancellationToken = default);
        Task<ImportResult> ImportAsync(Stream source, IProgress<ImportProgress>? progress = null, CancellationToken cancellationToken = default);
    }
}
