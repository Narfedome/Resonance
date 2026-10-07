using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Resonance.Components.Dialogs
{
    /// <summary>
    /// Logique du dialogue confirmation/info/erreur générique (ConfirmDialog n'est qu'un wrapper
    /// XAML lié dessus). CancelLabel null = dialogue à un seul bouton (info/erreur, cf.
    /// BaseViewModel.ShowErrorAsync/ShowInfoAsync).
    /// </summary>
    public partial class ConfirmDialogViewModel : DialogViewModel<bool>
    {
        protected override bool CancelResult => false;

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string message = string.Empty;

        [ObservableProperty]
        private string confirmLabel = string.Empty;

        [ObservableProperty]
        private string? cancelLabel;

        public bool HasCancel => CancelLabel != null;
        public int ConfirmButtonColumnSpan => HasCancel ? 1 : 2;

        public ConfirmDialogViewModel(string title, string message, string confirmLabel, string? cancelLabel)
        {
            Title = title;
            Message = message;
            ConfirmLabel = confirmLabel;
            CancelLabel = cancelLabel;
        }

        [RelayCommand]
        public void Confirm() => Close(true);
    }
}
