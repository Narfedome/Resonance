namespace Resonance.Core.Models.Feedback;

/// <summary>Ce dont parle un message « Nous écrire » - décide de l'étiquette du ticket (voir le relais,
/// relay/ du dépôt Support) et des consignes affichées sous les champs.</summary>
public enum FeedbackKind
{
    Bug,
    /// <summary>Utilisé par Mordheim Ledger (donnée fausse) - gardé pour rester identique au contrat du relais commun.</summary>
    Content,
    /// <summary>Texte de l'interface mal traduit.</summary>
    Translation,
    Idea,
    Other
}

/// <summary>Un message envoyé depuis l'appli, sans compte : le relais en fait un ticket dans le dépôt privé de
/// support. <see cref="Id"/> est fixé à la création et réutilisé à chaque nouvel essai (file hors connexion,
/// <see cref="Services.FeedbackOutbox"/>) : le relais ignore un second envoi du même message.</summary>
public sealed class FeedbackReport
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Appli d'origine (« mordheim », « resonance ») : même relais pour les deux.</summary>
    public string App { get; set; } = "resonance";

    public FeedbackKind Kind { get; set; } = FeedbackKind.Bug;

    public string Summary { get; set; } = string.Empty;

    public string Details { get; set; } = string.Empty;

    /// <summary>Écran ou texte concerné - seulement pour Content/Translation.</summary>
    public string? Where { get; set; }

    /// <summary>Null quand le joueur a décoché « Joindre les informations techniques ».</summary>
    public FeedbackTechnicalInfo? Technical { get; set; }

    public List<FeedbackAttachment> Screenshots { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Contexte joint par défaut (case « Joindre les informations techniques ») - le joueur peut le voir
/// avant d'envoyer (« Voir ce qui est envoyé »).</summary>
public sealed class FeedbackTechnicalInfo
{
    public string AppVersion { get; set; } = string.Empty;
    public string Build { get; set; } = string.Empty;
    public string Platform { get; set; } = string.Empty;
    public string OsVersion { get; set; } = string.Empty;
    public string DeviceModel { get; set; } = string.Empty;
    public string Language { get; set; } = string.Empty;

    /// <summary>Empreinte du contenu officiel installé (SeedContent) : dit quelle version des JSON le joueur a.</summary>
    public string? ContentFingerprint { get; set; }

    /// <summary>Dernières lignes du journal d'erreurs de l'appli, s'il y en a.</summary>
    public string? LogTail { get; set; }
}

/// <summary>Capture d'écran jointe, déjà réduite par l'appli (voir FeedbackRules.MaxScreenshotBytes).</summary>
public sealed class FeedbackAttachment
{
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "image/jpeg";
    public string Base64 { get; set; } = string.Empty;
}
