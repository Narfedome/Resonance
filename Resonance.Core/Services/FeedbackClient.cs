using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Resonance.Core.Models.Feedback;

namespace Resonance.Core.Services;

/// <summary>Issue d'un envoi au relais.</summary>
public enum FeedbackSendResult
{
    Sent,
    /// <summary>Pas de réseau, délai dépassé ou relais indisponible (5xx) : le message est gardé et réessayé.</summary>
    Retry,
    /// <summary>Trop d'envois depuis cet appareil (429) : gardé aussi, réessayé plus tard.</summary>
    RateLimited,
    /// <summary>Refusé pour de bon (4xx) : message invalide, inutile de le renvoyer tel quel.</summary>
    Rejected
}

/// <summary>Envoie un <see cref="FeedbackReport"/> au relais (relay/ du dépôt Support) en JSON. Sans dépendance MAUI :
/// l'appli lui passe l'adresse du relais et la clé d'appli (un identifiant public qui écarte les robots, pas un
/// secret - le jeton GitHub ne quitte jamais le relais).</summary>
public sealed class FeedbackClient
{
    public const string AppKeyHeader = "X-Feedback-Key";

    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _http;
    private readonly Uri _endpoint;
    private readonly string _appKey;

    public FeedbackClient(HttpClient http, Uri endpoint, string appKey)
    {
        _http = http;
        _endpoint = endpoint;
        _appKey = appKey;
    }

    public async Task<FeedbackSendResult> SendAsync(FeedbackReport report, CancellationToken cancellationToken = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _endpoint)
            {
                Content = JsonContent.Create(report, options: JsonOptions)
            };
            request.Headers.Add(AppKeyHeader, _appKey);
            using var response = await _http.SendAsync(request, cancellationToken);
            return Classify(response.StatusCode);
        }
        catch (HttpRequestException)
        {
            return FeedbackSendResult.Retry;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return FeedbackSendResult.Retry; // délai dépassé
        }
    }

    public static FeedbackSendResult Classify(HttpStatusCode status) => (int)status switch
    {
        >= 200 and < 300 => FeedbackSendResult.Sent,
        409 => FeedbackSendResult.Sent, // déjà reçu (même Id) : rien à renvoyer
        429 => FeedbackSendResult.RateLimited,
        408 => FeedbackSendResult.Retry,
        >= 400 and < 500 => FeedbackSendResult.Rejected,
        _ => FeedbackSendResult.Retry
    };
}
