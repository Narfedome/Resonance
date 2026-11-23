using Resonance.Data.Entities;
using Resonance.Models.Library;
using SQLite;

namespace Resonance.Data
{
    public class AppDatabase
    {
        private readonly SQLiteAsyncConnection _db;
        public SQLiteAsyncConnection Connection => _db;

        private readonly IReadOnlyList<string> _defaultTrackCategories;

        /// <summary>
        /// Création des tables, migrations et seed, lancés dès la construction. Les services de
        /// données l'attendent en tête de chaque méthode : SQLiteAsyncConnection ne garantit pas
        /// l'ordre d'exécution entre opérations, une requête partie avant la fin de l'init pouvait
        /// tomber sur "no such table" au premier lancement.
        /// </summary>
        public Task Initialization { get; }

        /// <param name="defaultTrackCategories">Catégories de tracks seedées au premier lancement
        /// (localisées par l'appelant — la couche données ne dépend pas de la localisation).</param>
        public AppDatabase(string path, IEnumerable<string>? defaultTrackCategories = null)
        {
            _defaultTrackCategories = defaultTrackCategories?.ToList() ?? new List<string>();
            _db = new SQLiteAsyncConnection(path);
            Initialization = InitializeAsync();
        }

        private async Task InitializeAsync()
        {
            // Migrations : ajout des colonnes manquantes sur les DB existantes, AVANT CreateTableAsync.
            // CreateTableAsync complète lui-même une table existante, mais avec des colonnes nullables
            // sans valeur par défaut : passé en premier, il rendait ces migrations sans effet (colonne
            // déjà là), si bien qu'une ancienne base gardait NULL partout (IsLooping lu comme faux,
            // Position jamais rattrapée). Une table absente est ignorée ici : CreateTableAsync la crée
            // ensuite complète.
            await AddColumnIfMissingAsync("SceneTrackEntity", "IsLooping INTEGER NOT NULL DEFAULT 1");
            await AddColumnIfMissingAsync("SceneTrackEntity", "FadeIn INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync("SceneTrackEntity", "FadeOut INTEGER NOT NULL DEFAULT 0");
            await AddColumnIfMissingAsync("TrackEntity", "Hash TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync("TrackEntity", "Category TEXT NOT NULL DEFAULT ''");
            await AddColumnIfMissingAsync("CategoryEntity", "LibraryType TEXT NOT NULL DEFAULT ''");

            // Position ajoutée pour le glisser-déposer de l'accordéon Campagne/Chapitre/Scène : sur
            // une DB existante, toutes les lignes prendraient sinon Position=0 (le DEFAULT de
            // l'ALTER TABLE), et l'ordre affiché deviendrait indéterminé (dépendant de l'ordre de
            // retour SQLite, non garanti) au lieu de rester celui d'avant la mise à jour. D'où le
            // backfill par Id ci-dessous : quand la colonne vient d'être ajoutée, ou quand une
            // migration passée par l'ancien ordre (cf. plus haut) l'a laissée à NULL.
            bool campaignPositionAdded = await AddColumnIfMissingAsync("CampaignEntity", "Position INTEGER NOT NULL DEFAULT 0");
            bool sessionPositionAdded = await AddColumnIfMissingAsync("SessionEntity", "Position INTEGER NOT NULL DEFAULT 0");
            bool scenePositionAdded = await AddColumnIfMissingAsync("SceneEntity", "Position INTEGER NOT NULL DEFAULT 0");

            await _db.CreateTableAsync<CampaignEntity>();
            await _db.CreateTableAsync<SessionEntity>();
            await _db.CreateTableAsync<SceneEntity>();
            await _db.CreateTableAsync<CharacterEntity>();
            await _db.CreateTableAsync<SpellEntity>();
            await _db.CreateTableAsync<TrackEntity>();
            await _db.CreateTableAsync<SceneTrackEntity>();
            await _db.CreateTableAsync<CharacterSpellEntity>();
            await _db.CreateTableAsync<CategoryEntity>();

            campaignPositionAdded |= await HasNullPositionAsync("CampaignEntity");
            sessionPositionAdded |= await HasNullPositionAsync("SessionEntity");
            scenePositionAdded |= await HasNullPositionAsync("SceneEntity");

            // Purge + seed regroupés dans une transaction : plusieurs etapes liees (purge des
            // orphelins puis rattachement retroactif puis seed conditionne par le resultat de la
            // purge) - une interruption en plein milieu laisserait la DB dans un etat intermediaire
            // incoherent (ex: categories rattachees mais seed jamais fait, relance ratee ensuite car
            // le compte ne serait plus a 0).
            await _db.RunInTransactionAsync(conn =>
            {
                // Purge unique des lignes orphelines laissées par les suppressions sans cascade des
                // versions précédentes (supprimer une campagne ne supprimait ni ses chapitres, ni
                // leurs scènes, ni leurs pistes). L'ordre importe : parents d'abord.
                conn.Execute("DELETE FROM SessionEntity WHERE CampaignId NOT IN (SELECT Id FROM CampaignEntity)");
                // SessionId != 0 : exclut la scène orpheline du Mixer (SceneDataService.
                // GetOrCreateOrphanSceneAsync, OrphanSceneSessionId = 0), volontairement sans
                // chapitre parent - elle serait sinon reconnue comme orpheline "involontaire" et
                // supprimée à chaque démarrage.
                conn.Execute("DELETE FROM SceneEntity WHERE SessionId != 0 AND SessionId NOT IN (SELECT Id FROM SessionEntity)");
                conn.Execute("DELETE FROM SceneTrackEntity WHERE SceneId NOT IN (SELECT Id FROM SceneEntity)");
                conn.Execute("DELETE FROM SceneTrackEntity WHERE TrackId NOT IN (SELECT Id FROM TrackEntity)");

                // Bases déjà passées par l'ancien ordre de migration (cf. InitializeAsync) : colonnes
                // ajoutées par SQLite-net, restées à NULL. Une piste de scène encore à NULL n'a jamais
                // été rechargée dans le mixeur depuis (il réécrit ses réglages en quittant la scène) :
                // elle reçoit les valeurs prévues, dont la boucle activée, au lieu d'une boucle
                // désactivée que l'utilisateur n'a jamais choisie.
                conn.Execute("UPDATE SceneTrackEntity SET IsLooping = 1 WHERE IsLooping IS NULL");
                conn.Execute("UPDATE SceneTrackEntity SET FadeIn = 0 WHERE FadeIn IS NULL");
                conn.Execute("UPDATE SceneTrackEntity SET FadeOut = 0 WHERE FadeOut IS NULL");
                conn.Execute("UPDATE TrackEntity SET Hash = '' WHERE Hash IS NULL");
                conn.Execute("UPDATE TrackEntity SET Category = '' WHERE Category IS NULL");

                // Toute catégorie créée avant l'introduction de LibraryType ne pouvait être que pour des
                // tracks (les sorts n'ont pas encore de catégories) : on les rattache rétroactivement.
                conn.Execute($"UPDATE CategoryEntity SET LibraryType = '{nameof(Track)}' WHERE LibraryType = ''");

                // Seed initial des catégories de tracks (une seule fois, aucune catégorie de ce type) : les
                // 3 catégories par défaut + toute catégorie déjà utilisée par des tracks existantes (upgrade
                // depuis une DB qui n'avait pas encore cette table). Si l'utilisateur en supprime une
                // ensuite, elle ne revient pas au prochain lancement.
                // Stryker disable once all : requête traduite en SQL par SQLite-net (cf. Resonance.Tests/stryker-config.json)
                if (conn.Table<CategoryEntity>().Where(c => c.LibraryType == nameof(Track)).Count() == 0)
                {
                    var existingTrackCategories = conn.Table<TrackEntity>().ToList()
                        .Select(t => t.Category)
                        .Where(c => !string.IsNullOrWhiteSpace(c));

                    foreach (var name in _defaultTrackCategories.Union(existingTrackCategories, StringComparer.OrdinalIgnoreCase))
                        conn.Insert(new CategoryEntity { Name = name, LibraryType = nameof(Track) });
                }

                // Backfill par Id (= ordre d'insertion historique) plutôt qu'un ordre indéterminé.
                if (campaignPositionAdded)
                    conn.Execute("UPDATE CampaignEntity SET Position = " +
                        "(SELECT COUNT(*) FROM CampaignEntity c2 WHERE c2.Id < CampaignEntity.Id)");
                if (sessionPositionAdded)
                    conn.Execute("UPDATE SessionEntity SET Position = " +
                        "(SELECT COUNT(*) FROM SessionEntity s2 WHERE s2.CampaignId = SessionEntity.CampaignId AND s2.Id < SessionEntity.Id)");
                if (scenePositionAdded)
                    conn.Execute("UPDATE SceneEntity SET Position = " +
                        "(SELECT COUNT(*) FROM SceneEntity sc2 WHERE sc2.SessionId = SceneEntity.SessionId AND sc2.Id < SceneEntity.Id)");
            });
        }

        /// <summary>
        /// Ajoute la colonne si la table existe et ne l'a pas encore. Renvoie vrai si elle vient
        /// d'être ajoutée. Présence testée via PRAGMA table_info plutôt qu'en interceptant l'erreur
        /// "duplicate column" : un vrai problème (disque plein, DB corrompue...) remonte tel quel.
        /// </summary>
        private async Task<bool> AddColumnIfMissingAsync(string table, string columnDefinition)
        {
            var columns = await _db.GetTableInfoAsync(table);
            if (columns.Count == 0)
                return false; // table absente : CreateTableAsync la crée ensuite avec toutes ses colonnes

            var column = columnDefinition.Split(' ')[0];
            if (columns.Any(c => string.Equals(c.Name, column, StringComparison.OrdinalIgnoreCase)))
                return false;

            await _db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {columnDefinition}");
            return true;
        }

        private async Task<bool> HasNullPositionAsync(string table) =>
            await _db.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM {table} WHERE Position IS NULL") > 0;
    }
}
