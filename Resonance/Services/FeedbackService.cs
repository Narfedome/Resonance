using Resonance.Core.Models.Feedback;
using Resonance.Core.Services;

namespace Resonance.Services;

/// <summary>Côté appli du formulaire « Nous écrire » : adresse du relais (relay/ du dépôt Support), informations
/// techniques, captures d'écran et file hors connexion (<see cref="FeedbackOutbox"/>, vidée au lancement).</summary>
public static class FeedbackService
{
    /// <summary>Adresse du relais déployé (voir relay/README.md du dépôt Support). Tant qu'elle est nulle,
    /// Paramètres ouvre encore l'ancien formulaire Google : on peut livrer l'appli avant le relais.</summary>
    public static readonly Uri? Endpoint = new("https://ancient-paper-dc0d.narfedome-support.workers.dev/report");

    /// <summary>Identifiant public de l'appli auprès du relais (variable APP_KEYS du Worker) : écarte les robots,
    /// ce n'est pas un secret.</summary>
    public const string AppKey = "resonance";

    public static readonly Uri LegacyFormUri =
        new("https://docs.google.com/forms/d/e/1FAIpQLSdg2q1o01eGZsFvd0qIwOqYEVKDwBikQ0g7FWLSWHenKqeW0g/viewform?usp=dialog");

    public static bool IsConfigured => Endpoint is not null;

    public static FeedbackOutbox Outbox { get; } = new(FileSystem.AppDataDirectory);

    private static readonly Lazy<FeedbackClient?> Client = new(() => Endpoint is null
        ? null
        : new FeedbackClient(new HttpClient { Timeout = TimeSpan.FromSeconds(30) }, Endpoint, AppKey));

    /// <summary>Envoie le message ; s'il ne part pas pour une raison passagère (réseau, trop d'envois), il est
    /// gardé et renvoyé au prochain lancement.</summary>
    public static async Task<FeedbackSendResult> SendAsync(FeedbackReport report)
    {
        if (Client.Value is not { } client) return FeedbackSendResult.Rejected;
        var result = await client.SendAsync(report);
        if (result is FeedbackSendResult.Retry or FeedbackSendResult.RateLimited)
            await Outbox.EnqueueAsync(report);
        return result;
    }

    /// <summary>Au lancement : renvoie les messages en attente, sans jamais gêner le démarrage.</summary>
    public static async Task FlushOutboxAsync()
    {
        if (Client.Value is not { } client || E2EMode.IsEnabled) return;
        try
        {
            await Outbox.FlushAsync(report => client.SendAsync(report));
        }
        catch (Exception ex)
        {
            CrashLogger.LogException("FeedbackService.FlushOutboxAsync", ex);
        }
    }

    public static FeedbackTechnicalInfo BuildTechnicalInfo()
    {
        return new FeedbackTechnicalInfo
        {
            AppVersion = AppInfo.Current.VersionString,
            Build = AppInfo.Current.BuildString,
            Platform = DeviceInfo.Current.Platform.ToString(),
            OsVersion = DeviceInfo.Current.VersionString,
            DeviceModel = $"{DeviceInfo.Current.Manufacturer} {DeviceInfo.Current.Model}".Trim(),
            Language = LocalizationService.Instance.Culture.TwoLetterISOLanguageName,
            LogTail = FeedbackRules.Tail(ReadCrashLog())
        };
    }

    /// <summary>Ce que montre « Voir ce qui est envoyé » : exactement les champs joints.</summary>
    public static string Describe(FeedbackTechnicalInfo info)
    {
        var lines = new List<string>
        {
            $"App       {info.AppVersion} ({info.Build})",
            $"Platform  {info.Platform} {info.OsVersion}",
            $"Device    {info.DeviceModel}",
            $"Language  {info.Language}"
        };
        if (info.LogTail is { } log)
            lines.Add($"Log       {log.Split('\n').Length} lines");
        return string.Join('\n', lines);
    }

    private static string? ReadCrashLog()
    {
        try
        {
            var path = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
            return File.Exists(path) ? File.ReadAllText(path) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Galerie (ou fichier sous Windows) : jusqu'à <paramref name="limit"/> images, réduites à 1600 px.
    /// Une image encore trop lourde après réduction est ignorée.</summary>
    public static async Task<IReadOnlyList<FeedbackAttachment>> PickScreenshotsAsync(int limit)
    {
        if (limit <= 0) return [];
        var files = await MediaPicker.Default.PickPhotosAsync(new MediaPickerOptions
        {
            MaximumWidth = 1600,
            MaximumHeight = 1600,
            CompressionQuality = 80,
            SelectionLimit = limit
        });

        var result = new List<FeedbackAttachment>();
        foreach (var file in files.Take(limit))
        {
            await using var stream = await file.OpenReadAsync();
            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory);
            var base64 = Convert.ToBase64String(memory.ToArray());
            if (base64.Length > FeedbackRules.MaxScreenshotBytes) continue;
            result.Add(new FeedbackAttachment
            {
                FileName = file.FileName,
                ContentType = file.ContentType ?? "image/jpeg",
                Base64 = base64
            });
        }
        return result;
    }
}
