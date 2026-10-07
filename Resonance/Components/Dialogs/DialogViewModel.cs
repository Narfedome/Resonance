using CommunityToolkit.Mvvm.Input;

namespace Resonance.Components.Dialogs
{
    /// <summary>
    /// Base commune aux 6 ViewModels de dialogue (Confirm/Prompt/ActionSheet/ChannelSettings/
    /// CreateItem/TrackEdit) : chacun redéclarait à l'identique le même event CloseRequested et la
    /// même commande Cancel invoquant CloseRequested avec une valeur "annulé" fixe (false, null,
    /// -1...) - seule la logique de confirmation (Confirm/Save/Select, nom et corps) varie
    /// réellement d'un dialogue à l'autre, donc reste propre à chaque sous-classe.
    /// </summary>
    public abstract partial class DialogViewModel<TResult> : BaseViewModel
    {
        /// <summary>Signale au XAML wrapper (ConfirmDialog, PromptDialog...) de se refermer.</summary>
        public event Action<TResult>? CloseRequested;

        /// <summary>Un event ne peut être levé que depuis la classe qui le déclare, même par une
        /// sous-classe : ce point d'accès protégé remplace l'appel direct à CloseRequested?.Invoke
        /// que faisait chaque ViewModel avant cette factorisation.</summary>
        protected void Close(TResult result) => CloseRequested?.Invoke(result);

        /// <summary>Valeur envoyée à la fermeture par annulation (false, null, -1 selon le dialogue).</summary>
        protected abstract TResult CancelResult { get; }

        [RelayCommand]
        public void Cancel() => Close(CancelResult);
    }
}
