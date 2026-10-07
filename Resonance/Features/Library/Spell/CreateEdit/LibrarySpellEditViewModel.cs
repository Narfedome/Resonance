using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Models.Library;
using Resonance.Services;

namespace Resonance.Features.Library
{
    public partial class LibrarySpellEditViewModel
    : BaseViewModel, IQueryAttributable
    {
        private readonly ILibraryDataService _libraryDataService;

        public LibrarySpellEditViewModel(ILibraryDataService libraryDataService)
        {
            _libraryDataService = libraryDataService;
        }

        [ObservableProperty]
        private Spell? item;

        [ObservableProperty]
        private string title = string.Empty;

        public void ApplyQueryAttributes(IDictionary<string, object> query)
        {
            if (query.TryGetValue("Item", out var value) &&
           value is Spell item)
            {
                Item = item;
                Title = Item.Id != 0 ? Loc["SpellEditTitle"] : Loc["SpellCreateTitle"];
            }
        }
        [RelayCommand]
        public async Task Save()
        {
            if (Item == null)
                return;
            
            await _libraryDataService.SaveLibraryItemAsync(Item);
            WeakReferenceMessenger.Default.Send(new LibraryUpdatedMessage());
            await Shell.Current.GoToAsync("..");
        }

        [RelayCommand]
        public async Task Cancel()
        {
            await Shell.Current.GoToAsync("..");
        }
    }
}
