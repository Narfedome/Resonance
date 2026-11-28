using System.Text.Json;
using Resonance.Core.Models.Feedback;

namespace Resonance.Core.Services;

/// <summary>Messages « Nous écrire » pas encore partis (hors connexion, relais indisponible, trop d'envois) : un
/// fichier JSON par message sous « &lt;racine&gt;/feedback-outbox/ », la racine étant le dossier de données de l'appli
/// (passée au constructeur comme pour WarriorPhotoStore, pour que Core reste sans dépendance MAUI). L'appli vide la
/// file au lancement (<see cref="FlushAsync"/>).</summary>
public sealed class FeedbackOutbox
{
    public const string FolderName = "feedback-outbox";

    private readonly string _folder;

    public FeedbackOutbox(string rootDirectory)
    {
        _folder = Path.Combine(rootDirectory, FolderName);
    }

    public int PendingCount => Directory.Exists(_folder) ? Directory.GetFiles(_folder, "*.json").Length : 0;

    public async Task EnqueueAsync(FeedbackReport report)
    {
        Directory.CreateDirectory(_folder);
        await File.WriteAllTextAsync(PathOf(report.Id), JsonSerializer.Serialize(report, FeedbackClient.JsonOptions));
    }

    /// <summary>Envoie les messages en attente, du plus ancien au plus récent. Un message parti ou refusé pour de bon
    /// quitte la file ; au premier échec passager (réseau, trop d'envois), on s'arrête et le reste attend le prochain
    /// essai. Un fichier illisible est supprimé plutôt que de bloquer la file. Renvoie le nombre de messages partis.</summary>
    public async Task<int> FlushAsync(Func<FeedbackReport, Task<FeedbackSendResult>> send)
    {
        if (!Directory.Exists(_folder)) return 0;

        var pending = new List<FeedbackReport>();
        foreach (var file in Directory.GetFiles(_folder, "*.json"))
        {
            FeedbackReport? report = null;
            try
            {
                report = JsonSerializer.Deserialize<FeedbackReport>(await File.ReadAllTextAsync(file), FeedbackClient.JsonOptions);
            }
            catch (JsonException)
            {
            }
            if (report is null) File.Delete(file);
            else pending.Add(report);
        }

        var sent = 0;
        foreach (var report in pending.OrderBy(r => r.CreatedAt))
        {
            var result = await send(report);
            if (result is FeedbackSendResult.Retry or FeedbackSendResult.RateLimited) break;
            File.Delete(PathOf(report.Id));
            if (result == FeedbackSendResult.Sent) sent++;
        }
        return sent;
    }

    private string PathOf(string id) => Path.Combine(_folder, $"{id}.json");
}
