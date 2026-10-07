using CommunityToolkit.Mvvm.ComponentModel;
using Resonance.Models.Library;
using System.ComponentModel;

namespace Resonance.Features.Library
{
    /// <summary>
    /// Sélection multiple réutilisable pour les listes de la bibliothèque (Tracks, Spells...).
    /// Garde trace des éléments sélectionnés par Id - y compris ceux pas encore chargés à cause de la
    /// pagination - et synchronise le flag LibraryItem.IsSelected des éléments actuellement affichés.
    /// </summary>
    public partial class LibraryMultiSelection<TItem> : ObservableObject where TItem : LibraryItem
    {
        private readonly HashSet<int> _selectedIds = new();

        [ObservableProperty]
        private int selectedCount;

        public bool HasSelection => SelectedCount > 0;

        public IReadOnlyCollection<int> SelectedIds => _selectedIds;

        partial void OnSelectedCountChanged(int value) => OnPropertyChanged(nameof(HasSelection));

        /// <summary>À appeler quand un élément est ajouté à la liste affichée (pagination, import...).</summary>
        public void Track(TItem item)
        {
            item.IsSelected = _selectedIds.Contains(item.Id);
            item.PropertyChanged += OnItemPropertyChanged;
        }

        /// <summary>À appeler quand un élément est retiré de la liste affichée (suppression, rechargement...).</summary>
        public void Untrack(TItem item) => item.PropertyChanged -= OnItemPropertyChanged;

        public void Clear()
        {
            _selectedIds.Clear();
            SelectedCount = 0;
        }

        /// <summary>Vrai si tous les ids donnés sont déjà sélectionnés (utilisé pour basculer "Tout sélectionner").</summary>
        public bool ContainsAll(IEnumerable<int> ids) => ids.All(_selectedIds.Contains);

        /// <summary>Ajoute des ids à la sélection (ex: "Tout sélectionner") et resynchronise les éléments chargés.</summary>
        public void SelectIds(IEnumerable<int> ids, IEnumerable<TItem> loadedItems)
        {
            foreach (var id in ids)
                _selectedIds.Add(id);

            foreach (var item in loadedItems)
                item.IsSelected = _selectedIds.Contains(item.Id);

            SelectedCount = _selectedIds.Count;
        }

        /// <summary>Vide la sélection et décoche les éléments actuellement chargés.</summary>
        public void DeselectAll(IEnumerable<TItem> loadedItems)
        {
            _selectedIds.Clear();

            foreach (var item in loadedItems)
                item.IsSelected = false;

            SelectedCount = 0;
        }

        private void OnItemPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(LibraryItem.IsSelected) || sender is not TItem item)
                return;

            if (item.IsSelected)
                _selectedIds.Add(item.Id);
            else
                _selectedIds.Remove(item.Id);

            SelectedCount = _selectedIds.Count;
        }
    }
}
