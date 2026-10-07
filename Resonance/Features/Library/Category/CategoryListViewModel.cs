using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Models.Library;
using Resonance.Services;
using System.Collections.ObjectModel;

namespace Resonance.Features.Library
{
    public partial class CategoryListViewModel : BaseViewModel, IQueryAttributable
    {
        private readonly ILibraryDataService _libraryDataService;

        // Type de bibliothèque géré par cette instance de la page (Track, Spell...) - transmis par le
        // bouton "Gérer les catégories" de la vue appelante, les catégories sont scopées par type.
        private Type _libraryType = typeof(Track);

        public CategoryListViewModel(ILibraryDataService libraryDataService)
        {
            _libraryDataService = libraryDataService;
        }

        // Réutilise le même libellé que "Aucun dossier" dans PickImportCategoryAsync/LibraryTrackViewModel :
        // catégorie native représentant Track.Category vide, listée ici (en tête) mais non modifiable.
        private string NoFolderLabel => Loc["LibImportNoCategory"];

        [ObservableProperty] private ObservableCollection<CategoryRowItem> categoryNames = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedCategoryName))]
        [NotifyPropertyChangedFor(nameof(CanModifySelectedCategory))]
        private CategoryRowItem? selectedCategory;

        // Exposée en lecture seule pour ne pas toucher au reste de la classe (Rename/Delete, bindings
        // IsEnabled dans CategoryListPage.xaml) : la sélection réelle passe par SelectedCategory,
        // affectée à la main via SelectCategory. Pas de CollectionView.SelectedItem/SelectionMode
        // natif (cf. CategoryListPage.xaml) : sur Android, le fond de sélection natif reste teinté
        // par colorAccent quel que soit le style posé dessus - seul un fond opaque le masque, ce qui
        // empêche le simple "fond transparent + liseré qui devient bleu" voulu ici.
        public string? SelectedCategoryName => SelectedCategory?.Name;

        public bool CanModifySelectedCategory => SelectedCategory is { IsSystem: false };

        partial void OnSelectedCategoryChanged(CategoryRowItem? oldValue, CategoryRowItem? newValue)
        {
            if (oldValue != null) oldValue.IsSelected = false;
            if (newValue != null) newValue.IsSelected = true;
        }

        [RelayCommand]
        public void SelectCategory(CategoryRowItem item) => SelectedCategory = item;

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("LibraryType", out var value) && value is Type type)
                _libraryType = type;
        }

        public async Task InitializeAsync()
        {
            await Loading.RunAsync(LoadAsync);
        }

        private async Task LoadAsync()
        {
            var names = await _libraryDataService.GetCategoryNamesAsync(_libraryType);
            var rows = new List<CategoryRowItem> { new(NoFolderLabel, IsSystem: true) };
            rows.AddRange(names.Select(name => new CategoryRowItem(name)));
            CategoryNames = new ObservableCollection<CategoryRowItem>(rows);
        }

        // Remplace le bouton retour natif du Shell (cf. CategoryListPage.xaml, Shell.NavBarIsVisible="False").
        [RelayCommand]
        public async Task Back() => await Shell.Current.GoToAsync("..");

        [RelayCommand]
        public async Task Create()
        {
            var name = await ShowPromptAsync(Loc["DialogNewCategory"], Loc["PromptName"]);
            if (string.IsNullOrWhiteSpace(name)) return;

            var trimmed = name.Trim();
            if (string.Equals(trimmed, NoFolderLabel, StringComparison.OrdinalIgnoreCase))
            {
                await ShowInfoAsync(Loc["CategoriesHeader"], Loc["LibCategoryNoFolderReserved"]);
                return;
            }

            await _libraryDataService.EnsureCategoryAsync(_libraryType, trimmed);
            WeakReferenceMessenger.Default.Send(new LibraryUpdatedMessage());
            await LoadAsync();
        }

        [RelayCommand]
        public async Task Rename()
        {
            if (!CanModifySelectedCategory || SelectedCategoryName == null) return;

            var newName = await ShowPromptAsync(Loc["DialogRename"], Loc["PromptName"], initialValue: SelectedCategoryName);
            if (string.IsNullOrWhiteSpace(newName) || newName == SelectedCategoryName) return;

            await _libraryDataService.RenameCategoryAsync(_libraryType, SelectedCategoryName, newName.Trim());
            WeakReferenceMessenger.Default.Send(new LibraryUpdatedMessage());
            await LoadAsync();
        }

        [RelayCommand]
        public async Task Delete()
        {
            if (!CanModifySelectedCategory || SelectedCategoryName == null) return;
            if (!await ConfirmDeleteAsync(SelectedCategoryName)) return;

            await _libraryDataService.DeleteCategoryAsync(_libraryType, SelectedCategoryName);
            WeakReferenceMessenger.Default.Send(new LibraryUpdatedMessage());
            await LoadAsync();
            SelectedCategory = null;
        }
    }

    // Classe (pas record) : IsSelected doit être un ObservableProperty pilotable à la main pour la
    // bordure de mise en évidence (cf. CategoryListViewModel.OnSelectedCategoryChanged et
    // CategoryListPage.xaml), la sélection native du CollectionView étant évitée.
    public partial class CategoryRowItem : ObservableObject
    {
        public string Name { get; }
        public bool IsSystem { get; }

        [ObservableProperty]
        private bool isSelected;

        public CategoryRowItem(string Name, bool IsSystem = false)
        {
            this.Name = Name;
            this.IsSystem = IsSystem;
        }

        public override string ToString() => Name;
    }
}
