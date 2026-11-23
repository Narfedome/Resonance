using Resonance.Data;
using Resonance.Data.Entities;
using Resonance.Models.Library;
using SQLite;

namespace Resonance.Services
{
    public class LibraryDataService : ILibraryDataService
    {
        private readonly AppDatabase _db;

        public LibraryDataService(AppDatabase db)
        {
            _db = db;
        }

        public async Task<List<LibraryItem>> GetAllItemsTypeAsync(Type currentLibraryType)
        {
            // Toutes les méthodes attendent la fin des créations de tables/migrations :
            // SQLiteAsyncConnection ne garantit pas l'ordre d'exécution entre opérations, une
            // requête lancée trop tôt au premier démarrage pouvait tomber sur "no such table".
            await _db.Initialization;
            var result = new List<LibraryItem>();

            if (currentLibraryType == typeof(Track))
            {
                var tracks = await _db.Connection.Table<TrackEntity>().ToListAsync();
                result.AddRange(tracks.Select(t => t.ToModel()));
            }

            return result.OrderBy(x => x.Id).ToList();
        }

        public async Task SaveLibraryItemAsync(LibraryItem item)
        {
            await _db.Initialization;
            if (item is Track track)
                await SaveTrack(track);
        }

        private async Task SaveTrack(Track track)
        {
            var entity = track.ToEntity();

            if (entity.Id == 0)
            {
                await _db.Connection.InsertAsync(entity);
                track.Id = entity.Id; // 🔥 IMPORTANT
            }
            else
            {
                await _db.Connection.UpdateAsync(entity);
            }
        }

        public async Task DeleteLibraryItem(LibraryItem libraryItem)
        {
            await _db.Initialization;
            if (libraryItem is not Track track)
                return;

            // Les pistes de scène qui l'utilisaient partent avec elle (pas de FK ON DELETE CASCADE
            // avec sqlite-net) : sinon elles restaient orphelines jusqu'à la purge du lancement suivant.
            await _db.Connection.RunInTransactionAsync(conn =>
            {
                conn.Execute("DELETE FROM SceneTrackEntity WHERE TrackId = ?", track.Id);
                conn.Delete<TrackEntity>(track.Id);
            });
        }

        public async Task<List<Track>> DeleteAllTracksAsync()
        {
            await _db.Initialization;
            var tracks = await _db.Connection.Table<TrackEntity>().ToListAsync();

            await _db.Connection.RunInTransactionAsync(conn =>
            {
                conn.DeleteAll<SceneTrackEntity>();
                conn.DeleteAll<TrackEntity>();
            });

            return tracks.Select(t => t.ToModel()).ToList();
        }

        /// <summary>
        /// Ids de tous les éléments d'un type de bibliothèque (utilisé par "Tout sélectionner", y compris
        /// les éléments non encore chargés à cause de la pagination). Le filtre par catégorie ne s'applique
        /// qu'aux Track (seul type qui a des catégories pour l'instant).
        /// </summary>
        public async Task<List<int>> GetItemIdsAsync(Type currentLibraryType, string? category, string? search = null)
        {
            await _db.Initialization;
            if (currentLibraryType == typeof(Track))
            {
                var query = _db.Connection.Table<TrackEntity>();
                if (category != null)
                    // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
                    query = query.Where(t => t.Category == category);
                query = FilterByTitle(query, search);

                var tracks = await query.ToListAsync();
                return tracks.Select(t => t.Id).ToList();
            }

            return new List<int>();
        }

        /// <summary>
        /// Supprime plusieurs éléments de bibliothèque d'un coup (BD + liaisons associées), utilisé par la
        /// suppression multiple. Retourne les éléments supprimés (Title/FilePath...) pour le nettoyage des
        /// fichiers physiques par l'appelant.
        /// </summary>
        public async Task<List<LibraryItem>> DeleteItemsAsync(Type currentLibraryType, IEnumerable<int> ids)
        {
            await _db.Initialization;
            var deleted = new List<LibraryItem>();

            if (currentLibraryType != typeof(Track))
                return deleted;

            // Une seule transaction pour tout le lot : une erreur au milieu ne laisse ni une sélection
            // à moitié supprimée, ni des pistes de scène orphelines.
            await _db.Connection.RunInTransactionAsync(conn =>
            {
                foreach (var id in ids)
                {
                    var entity = conn.Find<TrackEntity>(id);
                    if (entity == null) continue;

                    conn.Execute("DELETE FROM SceneTrackEntity WHERE TrackId = ?", id);
                    conn.Delete<TrackEntity>(id);
                    deleted.Add(entity.ToModel());
                }
            });

            return deleted;
        }

        /// <param name="category">null = pas de filtre (toutes catégories confondues), "" = piste sans
        /// catégorie ("Aucun dossier"), sinon le nom exact de la catégorie.</param>
        /// <param name="search">Texte cherché dans le titre, sans tenir compte de la casse ni des accents
        /// (cf. TextSearch) ; null ou vide = pas de recherche.</param>
        public async Task<List<LibraryItem>> GetItemsPageAsync(Type currentLibraryType, int skip, int take, string? category = null, string? search = null)
        {
            await _db.Initialization;
            var result = new List<LibraryItem>();

            if (currentLibraryType == typeof(Track))
            {
                var query = _db.Connection.Table<TrackEntity>();

                if (category != null)
                    // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
                    query = query.Where(t => t.Category == category);
                query = FilterByTitle(query, search);

                // Trié par catégorie (Id en repli à égalité) plutôt que par simple ordre d'insertion :
                // permet à l'appelant (LibraryTrackViewModel) de regrouper visuellement les pistes par
                // catégorie au fil de la pagination, les pistes d'une même catégorie sortant toujours
                // consécutives quelle que soit la page.
                // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
                var tracks = await query
                    .OrderBy(t => t.Category)
                    .ThenBy(t => t.Id)
                    .Skip(skip)
                    .Take(take)
                    .ToListAsync();

                result.AddRange(tracks.Select(t => t.ToModel()));
            }

            return result;
        }

        private static AsyncTableQuery<TrackEntity> FilterByTitle(AsyncTableQuery<TrackEntity> query, string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
                return query;

            // Comparaison sur la forme de recherche du titre (minuscules, sans accents, cf. TextSearch) :
            // SQLite-net traduit Contains en instr(), sensible à la casse et aux accents.
            var text = TextSearch.Normalize(search);
            // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
            return query.Where(t => t.SearchTitle.Contains(text));
        }

        public async Task<Track?> FindTrackByHashAsync(string hash, int excludeId)
        {
            if (string.IsNullOrEmpty(hash))
                return null;

            await _db.Initialization;

            // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
            var entity = await _db.Connection.Table<TrackEntity>()
                .Where(t => t.Hash == hash && t.Id != excludeId)
                .FirstOrDefaultAsync();

            return entity?.ToModel();
        }

        public async Task<List<string>> GetCategoryNamesAsync(Type currentLibraryType)
        {
            await _db.Initialization;
            var typeName = currentLibraryType.Name;
            // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
            var categories = await _db.Connection.Table<CategoryEntity>().Where(c => c.LibraryType == typeName).ToListAsync();
            return categories.Select(c => c.Name).OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public async Task EnsureCategoryAsync(Type currentLibraryType, string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return;

            await _db.Initialization;
            var typeName = currentLibraryType.Name;
            // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
            var existing = await _db.Connection.Table<CategoryEntity>().Where(c => c.Name == name && c.LibraryType == typeName).FirstOrDefaultAsync();
            if (existing == null)
                await _db.Connection.InsertAsync(new CategoryEntity { Name = name, LibraryType = typeName });
        }

        public async Task RenameCategoryAsync(Type currentLibraryType, string oldName, string newName)
        {
            await _db.Initialization;
            var typeName = currentLibraryType.Name;
            // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
            var entity = await _db.Connection.Table<CategoryEntity>().Where(c => c.Name == oldName && c.LibraryType == typeName).FirstOrDefaultAsync();
            if (entity == null)
                return;

            // Transaction : interrompu entre les deux étapes, des pistes se retrouvaient rangées sous un
            // nom de catégorie qui n'existe pas (ou plus) dans la liste des catégories.
            await _db.Connection.RunInTransactionAsync(conn =>
            {
                if (currentLibraryType == typeof(Track))
                    conn.Execute("UPDATE TrackEntity SET Category = ? WHERE Category = ?", newName, oldName);

                // Si le nom cible existe déjà (même type), on fusionne au lieu de créer un doublon.
                // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
                var targetExists = conn.Table<CategoryEntity>().Where(c => c.Name == newName && c.LibraryType == typeName).Count() > 0;
                if (targetExists)
                {
                    conn.Delete<CategoryEntity>(entity.Id);
                }
                else
                {
                    entity.Name = newName;
                    conn.Update(entity);
                }
            });
        }

        public async Task DeleteCategoryAsync(Type currentLibraryType, string name)
        {
            await _db.Initialization;
            var typeName = currentLibraryType.Name;
            // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
            var entity = await _db.Connection.Table<CategoryEntity>().Where(c => c.Name == name && c.LibraryType == typeName).FirstOrDefaultAsync();
            await _db.Connection.RunInTransactionAsync(conn =>
            {
                if (entity != null)
                    conn.Delete<CategoryEntity>(entity.Id);

                // La catégorie est un simple tag : on détache les tracks sans les supprimer.
                if (currentLibraryType == typeof(Track))
                    conn.Execute("UPDATE TrackEntity SET Category = '' WHERE Category = ?", name);
            });
        }

        /// <summary>
        /// Met à jour UNIQUEMENT le chemin de vignette d'une track (écriture de cache de pochette
        /// en arrière-plan, cf. CoverArtService). Contrairement à SaveLibraryItemAsync, ne réécrit
        /// pas toute la ligne : une sauvegarde différée qui s'entrelace avec une édition utilisateur
        /// faite depuis une autre instance du modèle n'écraserait sinon ses autres champs (titre...).
        /// </summary>
        public async Task UpdateTrackImagePathAsync(int trackId, string imagePath)
        {
            await _db.Initialization;
            await _db.Connection.ExecuteAsync(
                "UPDATE TrackEntity SET ImagePath = ? WHERE Id = ?", imagePath, trackId);
        }

        public async Task<int> CountTracksWithFilePathAsync(string filePath, int excludeId)
        {
            await _db.Initialization;
            // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
            return await _db.Connection.Table<TrackEntity>()
                .Where(t => t.FilePath == filePath && t.Id != excludeId)
                .CountAsync();
        }

        public async Task<HashSet<string>> GetAllReferencedFilePathsAsync()
        {
            await _db.Initialization;
            var paths = new HashSet<string>();

            var tracks = await _db.Connection.Table<TrackEntity>().ToListAsync();
            foreach (var track in tracks)
            {
                if (!string.IsNullOrEmpty(track.FilePath)) paths.Add(track.FilePath);
                if (!string.IsNullOrEmpty(track.ImagePath)) paths.Add(track.ImagePath);
            }

            return paths;
        }
    }

    public interface ILibraryDataService
    {
        Task SaveLibraryItemAsync(LibraryItem item);
        Task DeleteLibraryItem(LibraryItem item);
        Task<List<Track>> DeleteAllTracksAsync();
        Task<List<int>> GetItemIdsAsync(Type currentLibraryType, string? category, string? search = null);
        Task<List<LibraryItem>> DeleteItemsAsync(Type currentLibraryType, IEnumerable<int> ids);
        Task<List<LibraryItem>> GetAllItemsTypeAsync(Type currentLibraryType);
        Task<List<LibraryItem>> GetItemsPageAsync(Type currentLibraryType, int skip, int take, string? category = null, string? search = null);
        Task<Track?> FindTrackByHashAsync(string hash, int excludeId);
        Task UpdateTrackImagePathAsync(int trackId, string imagePath);
        Task<int> CountTracksWithFilePathAsync(string filePath, int excludeId);
        Task<HashSet<string>> GetAllReferencedFilePathsAsync();
        Task<List<string>> GetCategoryNamesAsync(Type currentLibraryType);
        Task EnsureCategoryAsync(Type currentLibraryType, string name);
        Task RenameCategoryAsync(Type currentLibraryType, string oldName, string newName);
        Task DeleteCategoryAsync(Type currentLibraryType, string name);
    }
}
