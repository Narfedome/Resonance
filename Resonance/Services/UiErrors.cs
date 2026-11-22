using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using Resonance.Components.Dialogs;

namespace Resonance.Services;

/// <summary>Garde des tâches lancées par l'interface sans appelant pour récupérer leur exception : gestionnaires
/// <c>async void</c> (OnAppearing, OnNavigatedTo, drops...). Sans elle, une exception dans un <c>async void</c>
/// fait planter l'appli. Ici l'erreur est toujours écrite dans le journal (CrashLogger), et pour un chargement de
/// données l'utilisateur voit un message plutôt qu'un plantage ; les effets purement visuels restent muets.
/// Porté de Mordheim Ledger.</summary>
public static class UiErrors
{
    // Contextes dont l'échec a déjà été montré et qui n'ont pas réussi depuis. Fermer le message relance
    // OnNavigatedTo de l'écran (fermeture d'une popup), donc son chargement, qui échouerait de nouveau : sans ce
    // garde-fou, chaque OK rouvrirait aussitôt le même message.
    private static readonly HashSet<string> Reported = [];

    /// <summary>Exécute work ; une exception est journalisée (et affichée si showMessage), jamais relancée.</summary>
    public static async Task RunAsync(Func<Task> work, string context, bool showMessage = true)
    {
        try
        {
            await work();
            lock (Reported) Reported.Remove(context);
        }
        catch (OperationCanceledException)
        {
            // Annulation voulue (ex. LibraryTrackViewModel.CancelPendingLoad en quittant l'écran) : pas une erreur.
        }
        catch (Exception ex)
        {
            await ReportAsync(ex, context, showMessage);
        }
    }

    public static async Task ReportAsync(Exception ex, string context, bool showMessage = true)
    {
        CrashLogger.LogException(context, ex);
        if (!showMessage) return;
        lock (Reported)
            if (!Reported.Add(context)) return;

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                // Comme BaseViewModel.CurrentPage : Shell.Current est null pendant l'onboarding.
                var page = Application.Current?.Windows.FirstOrDefault()?.Page;
                if (page is null) return;
                var loc = LocalizationService.Instance;
                var popup = new ConfirmDialog(new ConfirmDialogViewModel(
                    loc["ErrorTitle"], string.Format(loc["UnexpectedErrorMessage"], ex.Message), loc["DialogOk"], null));
                await page.ShowPopupAsync<bool>(popup, new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = true }, CancellationToken.None);
            });
        }
        catch (Exception dialogError)
        {
            // Le message lui-même ne doit jamais devenir une nouvelle source de plantage.
            CrashLogger.LogException($"{context} (message d'erreur)", dialogError);
        }
    }
}
