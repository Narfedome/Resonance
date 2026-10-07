using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Resonance.Components.Dialogs
{
    /// <summary>Une option de la liste, avec son index d'origine : nécessaire pour distinguer deux
    /// options au libellé identique (ex. deux scènes/chapitres homonymes) une fois affichées via
    /// data binding, où on ne peut plus se reposer sur la position de l'élément dans la liste.</summary>
    public record ActionSheetOption(int Index, string Label);

    /// <summary>
    /// Logique de la liste de choix générique (ActionSheetDialog n'est qu'un wrapper XAML lié
    /// dessus). Ferme avec l'index de l'option choisie (-1 : annulation).
    /// </summary>
    public partial class ActionSheetDialogViewModel : DialogViewModel<int>
    {
        protected override int CancelResult => -1;

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string cancelLabel = string.Empty;

        public List<ActionSheetOption> Options { get; }

        public ActionSheetDialogViewModel(string title, IEnumerable<string> options, string cancelLabel)
        {
            Title = title;
            CancelLabel = cancelLabel;
            Options = options.Select((label, index) => new ActionSheetOption(index, label)).ToList();
        }

        [RelayCommand]
        public void Select(ActionSheetOption option) => Close(option.Index);
    }
}
