using Resonance.Data;
using Resonance.Data.Entities;
using Resonance.Models;

namespace Resonance.Services
{
    public class SceneDataService : ISceneDataService
    {
        private readonly AppDatabase _db;

        public SceneDataService(AppDatabase db)
        {
            _db = db;
        }

        // ── Campaigns ──────────────────────────────────────────────

        public async Task<List<Campaign>> GetCampaignsAsync()
        {
            await _db.Initialization;
            var entities = await _db.Connection.Table<CampaignEntity>().OrderBy(e => e.Position).ToListAsync();
            return entities.Select(e => e.ToModel()).ToList();
        }

        public async Task SaveCampaignAsync(Campaign campaign)
        {
            await _db.Initialization;
            var entity = campaign.ToEntity();
            if (entity.Id == 0)
            {
                await _db.Connection.InsertAsync(entity);
                campaign.Id = entity.Id;
            }
            else
            {
                await _db.Connection.UpdateAsync(entity);
            }
        }

        // Les suppressions cascadent manuellement (sqlite-net ne gère pas les FK ON DELETE
        // CASCADE) : sans ça, chapitres, scènes et pistes de scène orphelins s'accumulaient
        // indéfiniment en base à chaque suppression d'un parent.

        public async Task DeleteCampaignAsync(Campaign campaign)
        {
            await _db.Initialization;
            // Transaction : une interruption entre ces 4 suppressions laisserait des chapitres/
            // scenes/pistes de scene orphelins en base (cf. la purge au demarrage dans AppDatabase).
            await _db.Connection.RunInTransactionAsync(conn =>
            {
                conn.Execute(
                    "DELETE FROM SceneTrackEntity WHERE SceneId IN (" +
                    "SELECT sc.Id FROM SceneEntity sc JOIN SessionEntity se ON sc.SessionId = se.Id WHERE se.CampaignId = ?)",
                    campaign.Id);
                conn.Execute(
                    "DELETE FROM SceneEntity WHERE SessionId IN (SELECT Id FROM SessionEntity WHERE CampaignId = ?)",
                    campaign.Id);
                conn.Execute(
                    "DELETE FROM SessionEntity WHERE CampaignId = ?", campaign.Id);
                conn.Delete<CampaignEntity>(campaign.Id);
            });
        }

        // ── Sessions (chapitres) ───────────────────────────────────

        public async Task<List<Session>> GetSessionsAsync(int campaignId)
        {
            await _db.Initialization;
            var entities = await _db.Connection.Table<SessionEntity>()
                .Where(e => e.CampaignId == campaignId)
                .OrderBy(e => e.Position)
                .ToListAsync();
            return entities.Select(e => e.ToModel()).ToList();
        }

        public async Task SaveSessionAsync(Session session)
        {
            await _db.Initialization;
            var entity = session.ToEntity();
            if (entity.Id == 0)
            {
                await _db.Connection.InsertAsync(entity);
                session.Id = entity.Id;
            }
            else
            {
                await _db.Connection.UpdateAsync(entity);
            }
        }

        public async Task DeleteSessionAsync(Session session)
        {
            await _db.Initialization;
            await _db.Connection.RunInTransactionAsync(conn =>
            {
                conn.Execute(
                    "DELETE FROM SceneTrackEntity WHERE SceneId IN (SELECT Id FROM SceneEntity WHERE SessionId = ?)",
                    session.Id);
                conn.Execute(
                    "DELETE FROM SceneEntity WHERE SessionId = ?", session.Id);
                conn.Delete<SessionEntity>(session.Id);
            });
        }

        // ── Scenes ────────────────────────────────────────────────

        public async Task<List<Scene>> GetScenesAsync(int sessionId)
        {
            await _db.Initialization;
            var entities = await _db.Connection.Table<SceneEntity>()
                .Where(e => e.SessionId == sessionId)
                .OrderBy(e => e.Position)
                .ToListAsync();
            return entities.Select(e => e.ToModel()).ToList();
        }

        public async Task SaveSceneAsync(Scene scene)
        {
            await _db.Initialization;
            var entity = scene.ToEntity();
            if (entity.Id == 0)
            {
                await _db.Connection.InsertAsync(entity);
                scene.Id = entity.Id;
            }
            else
            {
                await _db.Connection.UpdateAsync(entity);
            }
        }

        // SessionId=0 : aucun Chapitre réel n'a jamais cet Id (AutoIncrement démarre à 1), donc
        // cette valeur est un marqueur sûr et sans ambiguïté pour "scène orpheline" (pas de
        // campagne/chapitre parent). GetScenesAsync ne peut jamais la retourner (aucun chapitre
        // réel n'a l'Id 0), elle reste donc naturellement invisible dans l'accordéon
        // Campagne/Chapitre/Scène. Utilisée par le Mixer pour fonctionner sans passer par une
        // scène (session ponctuelle hors campagne) : une seule scène orpheline, créée au premier
        // besoin puis réutilisée telle quelle (son Titre n'est jamais affiché - le Mixer pilote
        // son propre libellé "Session libre" côté UI).
        public const int OrphanSceneSessionId = 0;

        public async Task<Scene> GetOrCreateOrphanSceneAsync()
        {
            await _db.Initialization;
            var existing = await _db.Connection.Table<SceneEntity>()
                .Where(e => e.SessionId == OrphanSceneSessionId)
                .FirstOrDefaultAsync();
            if (existing != null)
                return existing.ToModel();

            var entity = new SceneEntity { SessionId = OrphanSceneSessionId, Title = "Freeform" };
            await _db.Connection.InsertAsync(entity);
            return entity.ToModel();
        }

        public async Task DeleteSceneAsync(Scene scene)
        {
            await _db.Initialization;
            await _db.Connection.RunInTransactionAsync(conn =>
            {
                conn.Execute("DELETE FROM SceneTrackEntity WHERE SceneId = ?", scene.Id);
                conn.Delete<SceneEntity>(scene.Id);
            });
        }

        // ── SceneTracks ───────────────────────────────────────────

        public async Task<List<SceneTrack>> GetSceneTracksAsync(int sceneId)
        {
            await _db.Initialization;
            var sceneTrackEntities = await _db.Connection.Table<SceneTrackEntity>()
                .Where(e => e.SceneId == sceneId)
                .OrderBy(e => e.Position)
                .ToListAsync();

            var result = new List<SceneTrack>();

            foreach (var ste in sceneTrackEntities)
            {
                var trackEntity = await _db.Connection.Table<TrackEntity>()
                    .Where(t => t.Id == ste.TrackId)
                    .FirstOrDefaultAsync();

                if (trackEntity == null) continue;

                // Le volume du strip prime sur le volume par défaut de la track de bibliothèque.
                var track = trackEntity.ToModel();
                track.Volume = ste.Volume;

                result.Add(ste.ToModel(track));
            }

            return result;
        }

        public async Task SaveSceneTrackAsync(SceneTrack sceneTrack)
        {
            await _db.Initialization;
            var entity = sceneTrack.ToEntity();
            if (entity.Id == 0)
            {
                await _db.Connection.InsertAsync(entity);
                sceneTrack.Id = entity.Id;
            }
            else
            {
                await _db.Connection.UpdateAsync(entity);
            }
        }

        public async Task DeleteSceneTrackAsync(SceneTrack sceneTrack)
        {
            await _db.Initialization;
            await _db.Connection.DeleteAsync<SceneTrackEntity>(sceneTrack.Id);
        }

        public async Task UpdateSceneTrackAsync(int sceneTrackId, double volume, bool isLooping, bool autoPlay, bool fadeIn, bool fadeOut)
        {
            await _db.Initialization;
            var entity = await _db.Connection.FindAsync<SceneTrackEntity>(sceneTrackId);
            if (entity == null) return;
            entity.Volume = volume;
            entity.IsLooping = isLooping;
            entity.AutoPlay = autoPlay;
            entity.FadeIn = fadeIn;
            entity.FadeOut = fadeOut;
            await _db.Connection.UpdateAsync(entity);
        }

        /// <summary>
        /// Sauvegarde automatique du mixer : persiste les réglages du strip SANS toucher à
        /// l'AutoPlay, qui est un choix explicite de l'utilisateur (et serait sinon écrasé par
        /// l'état de lecture du moment).
        /// </summary>
        public async Task UpdateSceneTrackSettingsAsync(int sceneTrackId, double volume, bool isLooping, bool fadeIn, bool fadeOut)
        {
            await _db.Initialization;
            var entity = await _db.Connection.FindAsync<SceneTrackEntity>(sceneTrackId);
            if (entity == null) return;
            entity.Volume = volume;
            entity.IsLooping = isLooping;
            entity.FadeIn = fadeIn;
            entity.FadeOut = fadeOut;
            await _db.Connection.UpdateAsync(entity);
        }

        public async Task UpdateSceneTrackVolumeAsync(int sceneTrackId, float volume)
        {
            await _db.Initialization;
            var entity = await _db.Connection.FindAsync<SceneTrackEntity>(sceneTrackId);
            if (entity == null) return;
            entity.Volume = volume;
            await _db.Connection.UpdateAsync(entity);
        }

        // ── Réordonnancement (glisser-déposer) ────────────────────

        /// <summary>
        /// Réassigne Position = index pour chaque entité de la liste, dans l'ordre donné. En
        /// transaction : un déplacement touche potentiellement tous les frères (pas seulement
        /// l'élément glissé), une interruption à mi-chemin laisserait sinon des Position
        /// incohérentes entre eux (deux éléments à la même position, ou un trou). Un seul appelant
        /// par niveau (Campagne/Chapitre/Scène/SceneTrack) passe déjà les ids d'un seul groupe de
        /// frères (même parent) : aucune validation cross-parent n'est faite ici.
        /// </summary>
        private async Task ReorderAsync<T>(List<int> orderedIds) where T : IPositioned, new()
        {
            await _db.Initialization;
            await _db.Connection.RunInTransactionAsync(conn =>
            {
                for (int i = 0; i < orderedIds.Count; i++)
                {
                    var entity = conn.Find<T>(orderedIds[i]);
                    if (entity == null)
                        continue;

                    entity.Position = i;
                    conn.Update(entity);
                }
            });
        }

        public Task ReorderCampaignsAsync(List<int> orderedCampaignIds) => ReorderAsync<CampaignEntity>(orderedCampaignIds);
        public Task ReorderSessionsAsync(List<int> orderedSessionIds) => ReorderAsync<SessionEntity>(orderedSessionIds);
        public Task ReorderScenesAsync(List<int> orderedSceneIds) => ReorderAsync<SceneEntity>(orderedSceneIds);
        public Task ReorderSceneTracksAsync(List<int> orderedSceneTrackIds) => ReorderAsync<SceneTrackEntity>(orderedSceneTrackIds);
    }

    public interface ISceneDataService
    {
        Task<List<Campaign>> GetCampaignsAsync();
        Task SaveCampaignAsync(Campaign campaign);
        Task DeleteCampaignAsync(Campaign campaign);
        Task ReorderCampaignsAsync(List<int> orderedCampaignIds);

        Task<List<Session>> GetSessionsAsync(int campaignId);
        Task SaveSessionAsync(Session session);
        Task DeleteSessionAsync(Session session);
        Task ReorderSessionsAsync(List<int> orderedSessionIds);

        Task<List<Scene>> GetScenesAsync(int sessionId);
        Task SaveSceneAsync(Scene scene);
        Task DeleteSceneAsync(Scene scene);
        Task ReorderScenesAsync(List<int> orderedSceneIds);
        Task<Scene> GetOrCreateOrphanSceneAsync();

        Task<List<SceneTrack>> GetSceneTracksAsync(int sceneId);
        Task SaveSceneTrackAsync(SceneTrack sceneTrack);
        Task DeleteSceneTrackAsync(SceneTrack sceneTrack);
        Task UpdateSceneTrackAsync(int sceneTrackId, double volume, bool isLooping, bool autoPlay, bool fadeIn, bool fadeOut);
        Task UpdateSceneTrackSettingsAsync(int sceneTrackId, double volume, bool isLooping, bool fadeIn, bool fadeOut);
        Task UpdateSceneTrackVolumeAsync(int sceneTrackId, float volume);
        Task ReorderSceneTracksAsync(List<int> orderedSceneTrackIds);
    }
}
