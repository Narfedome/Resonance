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
            await _db.CreateTableAsync<CampaignEntity>();
            await _db.CreateTableAsync<SessionEntity>();
            await _db.CreateTableAsync<SceneEntity>();
            await _db.CreateTableAsync<CharacterEntity>();
            await _db.CreateTableAsync<SpellEntity>();
            await _db.CreateTableAsync<TrackEntity>();
            await _db.CreateTableAsync<SceneTrackEntity>();
            await _db.CreateTableAsync<CharacterSpellEntity>();
            await _db.CreateTableAsync<CategoryEntity>();

            // Migrations : ajout des colonnes manquantes sur les DB existantes. Le catch ne cible
            // que l'erreur "duplicate column" (colonne déjà présente) : un vrai problème (disque
            // plein, DB corrompue...) doit remonter au lieu d'être avalé silencieusement, sans quoi
            // l'appli planterait plus loin de façon incompréhensible.
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
            // backfill par Id ci-dessous, exécuté une seule fois (uniquement quand la colonne vient
            // d'être ajoutée, cf. AddColumnIfMissingAsync).
            bool campaignPositionAdded = await AddColumnIfMissingAsync("CampaignEntity", "Position INTEGER NOT NULL DEFAULT 0");
            bool sessionPositionAdded = await AddColumnIfMissingAsync("SessionEntity", "Position INTEGER NOT NULL DEFAULT 0");
            bool scenePositionAdded = await AddColumnIfMissingAsync("SceneEntity", "Position INTEGER NOT NULL DEFAULT 0");

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

                // Toute catégorie créée avant l'introduction de LibraryType ne pouvait être que pour des
                // tracks (les sorts n'ont pas encore de catégories) : on les rattache rétroactivement.
                conn.Execute($"UPDATE CategoryEntity SET LibraryType = '{nameof(Track)}' WHERE LibraryType = ''");

                // Seed initial des catégories de tracks (une seule fois, aucune catégorie de ce type) : les
                // 3 catégories par défaut + toute catégorie déjà utilisée par des tracks existantes (upgrade
                // depuis une DB qui n'avait pas encore cette table). Si l'utilisateur en supprime une
                // ensuite, elle ne revient pas au prochain lancement.
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

        private async Task<bool> AddColumnIfMissingAsync(string table, string columnDefinition)
        {
            try
            {
                await _db.ExecuteAsync($"ALTER TABLE {table} ADD COLUMN {columnDefinition}");
                return true;
            }
            catch (SQLiteException ex) when (ex.Message.Contains("duplicate column", StringComparison.OrdinalIgnoreCase))
            {
                // Colonne déjà présente (DB déjà migrée) : rien à faire.
                return false;
            }
        }
    }
}
