using System.Net;
using System.Text.Json;
using Resonance.Core.Models.Feedback;
using Resonance.Core.Services;

namespace Resonance.Tests;

public class FeedbackRulesTests
{
    [Theory]
    [InlineData("Bloqué en Fin de partie", "Le bouton Suivant reste grisé.", true)]
    [InlineData("", "détail", false)]
    [InlineData("résumé", "   ", false)]
    public void CanSend_NeedsSummaryAndDetails(string summary, string details, bool expected) =>
        Assert.Equal(expected, FeedbackRules.CanSend(summary, details));

    [Fact]
    public void CanSend_RejectsTooLongSummary() =>
        Assert.False(FeedbackRules.CanSend(new string('a', FeedbackRules.MaxSummaryLength + 1), "détail"));

    [Fact]
    public void Tail_KeepsLastLines()
    {
        var log = string.Join("\r\n", Enumerable.Range(1, 50).Select(i => $"ligne {i}")) + "\r\n";
        var tail = FeedbackRules.Tail(log, 3);
        Assert.Equal("ligne 48\nligne 49\nligne 50", tail);
        Assert.Null(FeedbackRules.Tail("  "));
    }

    [Fact]
    public void Normalize_DropsWhereWhenKindDoesNotAskForIt()
    {
        var report = FeedbackRules.Normalize(new FeedbackReport
        {
            Kind = FeedbackKind.Idea, Summary = "  Idée  ", Details = " texte ", Where = "Répurgateurs"
        });
        Assert.Equal("Idée", report.Summary);
        Assert.Equal("texte", report.Details);
        Assert.Null(report.Where);

        var translation = FeedbackRules.Normalize(new FeedbackReport { Kind = FeedbackKind.Translation, Where = " Fanatique " });
        Assert.Equal("Fanatique", translation.Where);
    }
}

public class FeedbackClientTests
{
    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public HttpRequestMessage? Last { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Last = request;
            Body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return respond(request);
        }
    }

    [Theory]
    [InlineData(HttpStatusCode.Created, FeedbackSendResult.Sent)]
    [InlineData(HttpStatusCode.Conflict, FeedbackSendResult.Sent)]
    [InlineData(HttpStatusCode.TooManyRequests, FeedbackSendResult.RateLimited)]
    [InlineData(HttpStatusCode.BadRequest, FeedbackSendResult.Rejected)]
    [InlineData(HttpStatusCode.Unauthorized, FeedbackSendResult.Rejected)]
    [InlineData(HttpStatusCode.RequestTimeout, FeedbackSendResult.Retry)]
    [InlineData(HttpStatusCode.BadGateway, FeedbackSendResult.Retry)]
    public void Classify(HttpStatusCode status, FeedbackSendResult expected) =>
        Assert.Equal(expected, FeedbackClient.Classify(status));

    [Fact]
    public async Task SendAsync_PostsJsonWithAppKey()
    {
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Created));
        var client = new FeedbackClient(new HttpClient(handler), new Uri("https://relais.test/report"), "cle-appli");

        var result = await client.SendAsync(new FeedbackReport
        {
            Id = "abc", Kind = FeedbackKind.Translation, Summary = "Terme", Details = "Texte", Where = "Fanatique",
            Technical = new FeedbackTechnicalInfo { AppVersion = "1.5.2", Platform = "Android" }
        });

        Assert.Equal(FeedbackSendResult.Sent, result);
        Assert.Equal(HttpMethod.Post, handler.Last!.Method);
        Assert.Equal("cle-appli", handler.Last.Headers.GetValues(FeedbackClient.AppKeyHeader).Single());
        using var json = JsonDocument.Parse(handler.Body!);
        var root = json.RootElement;
        Assert.Equal("abc", root.GetProperty("id").GetString());
        Assert.Equal("translation", root.GetProperty("kind").GetString());
        Assert.Equal("1.5.2", root.GetProperty("technical").GetProperty("appVersion").GetString());
        Assert.Equal("Fanatique", root.GetProperty("where").GetString());
        Assert.False(root.TryGetProperty("screenshots", out var shots) && shots.GetArrayLength() > 0);
    }

    [Fact]
    public async Task SendAsync_NetworkFailure_IsRetry()
    {
        var handler = new StubHandler(_ => throw new HttpRequestException("hors connexion"));
        var client = new FeedbackClient(new HttpClient(handler), new Uri("https://relais.test/report"), "k");
        Assert.Equal(FeedbackSendResult.Retry, await client.SendAsync(new FeedbackReport()));
    }
}

public class FeedbackOutboxTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "feedback-tests-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private static FeedbackReport Report(string id, int minutesAgo) =>
        new() { Id = id, Summary = id, Details = "d", CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-minutesAgo) };

    [Fact]
    public async Task Flush_SendsOldestFirst_AndEmptiesQueue()
    {
        var outbox = new FeedbackOutbox(_root);
        await outbox.EnqueueAsync(Report("recent", 1));
        await outbox.EnqueueAsync(Report("ancien", 10));
        Assert.Equal(2, outbox.PendingCount);

        var order = new List<string>();
        var sent = await outbox.FlushAsync(r => { order.Add(r.Id); return Task.FromResult(FeedbackSendResult.Sent); });

        Assert.Equal(2, sent);
        Assert.Equal(["ancien", "recent"], order);
        Assert.Equal(0, outbox.PendingCount);
    }

    [Theory]
    [InlineData(FeedbackSendResult.Retry)]
    [InlineData(FeedbackSendResult.RateLimited)]
    public async Task Flush_StopsAtFirstTransientFailure_AndKeepsTheRest(FeedbackSendResult failure)
    {
        var outbox = new FeedbackOutbox(_root);
        await outbox.EnqueueAsync(Report("a", 3));
        await outbox.EnqueueAsync(Report("b", 2));
        await outbox.EnqueueAsync(Report("c", 1));

        var calls = 0;
        var sent = await outbox.FlushAsync(r =>
        {
            calls++;
            return Task.FromResult(r.Id == "b" ? failure : FeedbackSendResult.Sent);
        });

        Assert.Equal(1, sent);
        Assert.Equal(2, calls); // c n'est pas tenté
        Assert.Equal(2, outbox.PendingCount);
    }

    [Fact]
    public async Task Flush_DropsRejectedMessages()
    {
        var outbox = new FeedbackOutbox(_root);
        await outbox.EnqueueAsync(Report("invalide", 1));
        var sent = await outbox.FlushAsync(_ => Task.FromResult(FeedbackSendResult.Rejected));
        Assert.Equal(0, sent);
        Assert.Equal(0, outbox.PendingCount);
    }

    [Fact]
    public async Task Flush_DeletesUnreadableFile()
    {
        var outbox = new FeedbackOutbox(_root);
        Directory.CreateDirectory(Path.Combine(_root, FeedbackOutbox.FolderName));
        await File.WriteAllTextAsync(Path.Combine(_root, FeedbackOutbox.FolderName, "casse.json"), "{pas du json");
        await outbox.FlushAsync(_ => Task.FromResult(FeedbackSendResult.Sent));
        Assert.Equal(0, outbox.PendingCount);
    }

    [Fact]
    public async Task Enqueue_RoundTripsAllFields()
    {
        var outbox = new FeedbackOutbox(_root);
        await outbox.EnqueueAsync(new FeedbackReport
        {
            Id = "x", Kind = FeedbackKind.Content, Summary = "S", Details = "D", Where = "W",
            Screenshots = [new FeedbackAttachment { FileName = "c.jpg", Base64 = "AAAA" }]
        });
        FeedbackReport? read = null;
        await outbox.FlushAsync(r => { read = r; return Task.FromResult(FeedbackSendResult.Sent); });
        Assert.NotNull(read);
        Assert.Equal(FeedbackKind.Content, read.Kind);
        Assert.Equal("AAAA", Assert.Single(read.Screenshots).Base64);
    }
}
