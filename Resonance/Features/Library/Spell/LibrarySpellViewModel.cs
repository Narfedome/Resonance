using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Models.Library;
using Resonance.Services;
using System.Collections.ObjectModel;

namespace Resonance.Features.Library
{
    public partial class LibrarySpellViewModel : BaseViewModel
    {
        private readonly ILibraryPickerNavigationService _navigation;
        private readonly ILibraryDataService _libraryDataService;
        [ObservableProperty]
        public ObservableCollection<Spell> spellItems = new();

        [ObservableProperty]
        private Spell? selectedSpellItem;

        public LibrarySpellViewModel(ILibraryPickerNavigationService navigation, ILibraryDataService libraryDataService)
        {
            _navigation = navigation;
            _libraryDataService = libraryDataService;
            WeakReferenceMessenger.Default.Register<LibraryUpdatedMessage>(this,
            async (r, m) =>
            {
                await MainThread.InvokeOnMainThreadAsync(LoadData);
            });
        }

        public async Task InitializeAsync()
        {
            await Loading.RunAsync(LoadData);
        }

        private async Task LoadData()
        {
            var items = await _libraryDataService.GetAllItemsTypeAsync(typeof(Spell));

            SpellItems.Clear();

            foreach (var item in items)
                SpellItems.Add((Spell)item);

            SelectedSpellItem = SpellItems.FirstOrDefault();
        }

        [RelayCommand]
        public async Task ConfirmSelection()
        {
            if (SelectedSpellItem is Spell spellItem)
                await _navigation.ClosePickerAsync(spellItem);
        }

        [RelayCommand]
        public async Task Cancel()
        {
            await _navigation.ClosePickerAsync(null);
        }

        [RelayCommand]
        public async Task Delete()
        {
            if (SelectedSpellItem == null)
                return;

            var item = SelectedSpellItem;

            if (!await ConfirmDeleteAsync(item.Title))
                return;

            await _libraryDataService.DeleteLibraryItem(item);

            SpellItems.Remove(item);
            SelectedSpellItem = null;
        }

        [RelayCommand]
        public async Task Edit()
        {
            if (SelectedSpellItem == null)
                return;

            var copy = SelectedSpellItem.Clone();
            await Shell.Current.GoToAsync(nameof(LibrarySpellEditPage),
            new Dictionary<string, object>
            {
                 { "Item", (Spell)copy }
            });
        }

        [RelayCommand]
        public async Task Create()
        {
            var newItem = new Spell();

                await Shell.Current.GoToAsync(nameof(LibrarySpellEditPage),
                    new Dictionary<string, object>
                    {
                       { "Item", (Spell)newItem }
                    });
        }
    } 
}
