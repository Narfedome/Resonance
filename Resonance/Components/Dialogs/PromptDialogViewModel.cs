using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Resonance.Components.Dialogs
{
    /// <summary>Logique du dialogue de saisie libre (PromptDialog n'est qu'un wrapper XAML lié dessus).</summary>
    public partial class PromptDialogViewModel : DialogViewModel<string?>
    {
        protected override string? CancelResult => null;

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string message = string.Empty;

        [ObservableProperty]
        private string placeholder = string.Empty;

        [ObservableProperty]
        private string text = string.Empty;

        [ObservableProperty]
        private string confirmLabel = string.Empty;

        [ObservableProperty]
        private string cancelLabel = string.Empty;

        public bool HasMessage => !string.IsNullOrEmpty(Message);

        public PromptDialogViewModel(string title, string message, string placeholder, string initialValue, string confirmLabel, string cancelLabel)
        {
            Title = title;
            Message = message;
            Placeholder = placeholder;
            Text = initialValue;
            ConfirmLabel = confirmLabel;
            CancelLabel = cancelLabel;
        }

        [RelayCommand]
        public void Confirm() => Close(Text);
    }
}
