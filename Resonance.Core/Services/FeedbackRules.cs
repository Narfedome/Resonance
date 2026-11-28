using Resonance.Core.Models.Feedback;

namespace Resonance.Core.Services;

/// <summary>Règles du formulaire « Nous écrire » - mêmes limites que le relais (relay/src/index.ts du dépôt Support),
/// qui refuse tout message qui les dépasse : les garder alignées.</summary>
public static class FeedbackRules
{
    public const int MaxSummaryLength = 120;
    public const int MaxDetailsLength = 5000;
    public const int MaxWhereLength = 200;
    public const int MaxScreenshots = 3;

    /// <summary>Par capture, une fois encodée : une capture d'écran réduite à 1600 px tient largement dedans.</summary>
    public const int MaxScreenshotBytes = 1_500_000;

    /// <summary>Lignes du journal d'erreurs jointes aux informations techniques.</summary>
    public const int LogTailLines = 40;

    /// <summary>« Envoyer » n'est actif qu'avec une phrase et un détail, dans les limites.</summary>
    public static bool CanSend(string? summary, string? details) =>
        !string.IsNullOrWhiteSpace(summary) && summary.Trim().Length <= MaxSummaryLength
        && !string.IsNullOrWhiteSpace(details) && details.Trim().Length <= MaxDetailsLength;

    /// <summary>Le champ « Où ? » n'a de sens que pour une règle, une donnée ou une traduction.</summary>
    public static bool AsksWhere(FeedbackKind kind) => kind is FeedbackKind.Content or FeedbackKind.Translation;

    /// <summary>Les <paramref name="lines"/> dernières lignes d'un journal, null s'il est vide.</summary>
    public static string? Tail(string? log, int lines = LogTailLines)
    {
        if (string.IsNullOrWhiteSpace(log)) return null;
        var all = log.Replace("\r\n", "\n").TrimEnd('\n').Split('\n');
        return string.Join('\n', all.Skip(Math.Max(0, all.Length - lines)));
    }

    /// <summary>Message prêt à partir : champs nettoyés, « Où ? » retiré quand le type ne le demande pas.</summary>
    public static FeedbackReport Normalize(FeedbackReport report)
    {
        report.Summary = report.Summary.Trim();
        report.Details = report.Details.Trim();
        report.Where = AsksWhere(report.Kind) && !string.IsNullOrWhiteSpace(report.Where) ? report.Where.Trim() : null;
        return report;
    }
}
