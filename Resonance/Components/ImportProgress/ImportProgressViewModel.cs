using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Services;

namespace Resonance.Components
{
    public partial class ImportProgressViewModel : ObservableObject
    {
        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string currentFileName = string.Empty;

        [ObservableProperty]
        private int processedCount;

        [ObservableProperty]
        private int totalCount;

        [ObservableProperty]
        private bool isCancelling;

        // Reste à false par défaut (Import ne s'y abonne pas encore) : le bouton ne doit apparaître
        // que pour les opérations qui réagissent réellement à CancelRequested, sinon il resterait
        // affiché sans rien faire.
        [ObservableProperty]
        private bool showCancelButton;

        public string ProgressText => $"{ProcessedCount} / {TotalCount}";

        public string CancelButtonText => IsCancelling
            ? LocalizationService.Instance["ImportProgressCancelling"]
            : LocalizationService.Instance["BtnCancel"];

        /// <summary>Levé quand l'utilisateur tape sur le bouton Annuler - les appelants (Export/Import)
        /// y réagissent en annulant leur propre CancellationTokenSource.</summary>
        public event Action? CancelRequested;

        partial void OnProcessedCountChanged(int value) => OnPropertyChanged(nameof(ProgressText));
        partial void OnTotalCountChanged(int value) => OnPropertyChanged(nameof(ProgressText));
        partial void OnIsCancellingChanged(bool value) => OnPropertyChanged(nameof(CancelButtonText));

        [RelayCommand]
        private void Cancel()
        {
            // Ignore un second tap : l'annulation est déjà demandée, on ne veut pas relever
            // CancelRequested plusieurs fois (l'appelant pourrait par ex. re-disposer un
            // CancellationTokenSource déjà annulé).
            if (IsCancelling)
                return;

            IsCancelling = true;
            CancelRequested?.Invoke();
        }
    }
}
