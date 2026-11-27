using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Components.Dialogs;
using Resonance.Core.Models.Feedback;
using Resonance.Core.Services;
using Resonance.Services;

namespace Resonance.Features.Feedback;

/// <summary>Un type de message proposé en haut du formulaire (puce radio).</summary>
public partial class FeedbackKindOption(FeedbackKind kind, string title, string description) : ObservableObject
{
    public FeedbackKind Kind { get; } = kind;
    public string Title { get; } = title;
    public string Description { get; } = description;

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>Capture d'écran jointe, avec sa miniature.</summary>
public sealed class FeedbackScreenshot(FeedbackAttachment attachment)
{
    public FeedbackAttachment Attachment { get; } = attachment;
    public ImageSource Image { get; } = ImageSource.FromStream(() => new MemoryStream(Convert.FromBase64String(attachment.Base64)));
}

/// <summary>Formulaire « Nous écrire » (même formulaire que Mordheim Ledger, maquette mockups/report-issue.html de ce
/// dépôt-là) : sans compte, le message part au relais commun qui en fait un ticket dans le dépôt privé de support. Résultat : true si le message est parti ou mis en attente.</summary>
public partial class FeedbackDialogViewModel : DialogViewModel<bool>
{
    private FeedbackTechnicalInfo? _technical;

    public FeedbackDialogViewModel()
    {
        Kinds =
        [
            new(FeedbackKind.Bug, Loc["FeedbackKindBug"], Loc["FeedbackKindBugDesc"]),
            new(FeedbackKind.Translation, Loc["FeedbackKindTranslation"], Loc["FeedbackKindTranslationDesc"]),
            new(FeedbackKind.Idea, Loc["FeedbackKindIdea"], Loc["FeedbackKindIdeaDesc"]),
            new(FeedbackKind.Other, Loc["FeedbackKindOther"], Loc["FeedbackKindOtherDesc"])
        ];
        Kinds[0].IsSelected = true;
    }

    protected override bool CancelResult => false;

    public IReadOnlyList<FeedbackKindOption> Kinds { get; }

    public string Subtitle => $"Resonance {AppInfo.Current.VersionString}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowWhere), nameof(DetailsLabel), nameof(DetailsHint), nameof(HasDetailsHint))]
    private FeedbackKind _kind = FeedbackKind.Bug;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _summary = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendCommand))]
    private string _details = string.Empty;

    [ObservableProperty]
    private string _where = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowEmailError))]
    private string _email = string.Empty;

    [ObservableProperty]
    private bool _attachTechnical = true;

    [ObservableProperty]
    private bool _showTechnical;

    [ObservableProperty]
    private string _technicalPreview = string.Empty;

    public ObservableCollection<FeedbackScreenshot> Screenshots { get; } = [];

    public bool CanAddScreenshot => Screenshots.Count < FeedbackRules.MaxScreenshots;

    public bool ShowWhere => FeedbackRules.AsksWhere(Kind);
    public bool ShowEmailError => !FeedbackRules.IsValidEmail(Email);
    public int MaxSummaryLength => FeedbackRules.MaxSummaryLength;
    public int MaxDetailsLength => FeedbackRules.MaxDetailsLength;
    public int MaxWhereLength => FeedbackRules.MaxWhereLength;

    public string DetailsLabel => Loc[Kind switch
    {
        FeedbackKind.Bug => "FeedbackDetailsBug",
        FeedbackKind.Translation => "FeedbackDetailsTranslation",
        FeedbackKind.Idea => "FeedbackDetailsIdea",
        _ => "FeedbackDetailsOther"
    }];

    public string DetailsHint => Kind switch
    {
        FeedbackKind.Bug => Loc["FeedbackDetailsBugHint"],
        FeedbackKind.Translation => Loc["FeedbackDetailsTranslationHint"],
        FeedbackKind.Idea => Loc["FeedbackDetailsIdeaHint"],
        _ => string.Empty
    };

    public bool HasDetailsHint => !string.IsNullOrEmpty(DetailsHint);

    [RelayCommand]
    private void SelectKind(FeedbackKindOption option)
    {
        foreach (var k in Kinds) k.IsSelected = ReferenceEquals(k, option);
        Kind = option.Kind;
    }

    [RelayCommand]
    private Task ToggleTechnical()
    {
        if (!ShowTechnical)
        {
            _technical ??= FeedbackService.BuildTechnicalInfo();
            TechnicalPreview = FeedbackService.Describe(_technical);
        }
        ShowTechnical = !ShowTechnical;
        return Task.CompletedTask;
    }

    partial void OnAttachTechnicalChanged(bool value)
    {
        if (!value) ShowTechnical = false;
    }

    [RelayCommand]
    private async Task AddScreenshot()
    {
        try
        {
            var picked = await FeedbackService.PickScreenshotsAsync(FeedbackRules.MaxScreenshots - Screenshots.Count);
            foreach (var attachment in picked) Screenshots.Add(new FeedbackScreenshot(attachment));
            OnPropertyChanged(nameof(CanAddScreenshot));
        }
        catch (Exception ex)
        {
            CrashLogger.LogException("FeedbackDialogViewModel.AddScreenshot", ex);
            ErrorMessage = ex.Message;
        }
    }

    [RelayCommand]
    private void RemoveScreenshot(FeedbackScreenshot screenshot)
    {
        Screenshots.Remove(screenshot);
        OnPropertyChanged(nameof(CanAddScreenshot));
    }

    private bool CanSend() => FeedbackRules.CanSend(Summary, Details);

    /// <summary>Issue de l'envoi, lue par SettingsViewModel une fois le dialog fermé pour afficher le message
    /// (pas de popup par-dessus ce dialog).</summary>
    public FeedbackSendResult? Result { get; private set; }

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task Send()
    {
        if (ShowEmailError)
        {
            ErrorMessage = Loc["FeedbackEmailInvalid"];
            return;
        }
        ErrorMessage = string.Empty;

        var result = FeedbackSendResult.Rejected;
        await Loading.RunAsync(async () =>
        {
            var report = FeedbackRules.Normalize(new FeedbackReport
            {
                App = "resonance",
                Kind = Kind,
                Summary = Summary,
                Details = Details,
                Where = Where,
                Email = Email,
                Language = LocalizationService.Instance.Culture.TwoLetterISOLanguageName,
                Technical = AttachTechnical ? _technical ?? FeedbackService.BuildTechnicalInfo() : null,
                Screenshots = Screenshots.Select(s => s.Attachment).ToList()
            });
            result = await FeedbackService.SendAsync(report);
        });

        if (result == FeedbackSendResult.Rejected)
        {
            // Refusé : le texte reste dans le formulaire pour que l'utilisateur le corrige ou le copie.
            ErrorMessage = Loc["FeedbackRejected"];
            return;
        }
        Result = result;
        Close(true);
    }
}
