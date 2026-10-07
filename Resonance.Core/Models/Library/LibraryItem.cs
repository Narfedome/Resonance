using CommunityToolkit.Mvvm.ComponentModel;

namespace Resonance.Models.Library
{
    public partial class LibraryItem : ObservableObject
    {

        [ObservableProperty]
        private int id; 

        [ObservableProperty]
        private string title = "";

        [ObservableProperty]
        private string imagePath = "";

        [ObservableProperty]
        private string filePath  ="";

        // État UI uniquement (sélection multiple dans la bibliothèque) - non persisté en BD.
        [ObservableProperty]
        private bool isSelected;

        // Distinct d'IsSelected (coche de sélection multiple) : représente l'unique item "tapé"
        // (SelectedTrackItem côté ViewModel), pilote la bordure de mise en évidence à la main plutôt
        // que par la sélection native du CollectionView (cf. LibraryTrackView.xaml - sur Android, le
        // fond de sélection natif reste teinté par colorAccent quel que soit le style posé dessus).
        // État UI uniquement, non persisté en BD.
        [ObservableProperty]
        private bool isPrimarySelected;

        public LibraryItem Clone()
        {
            return (LibraryItem)this.MemberwiseClone();
        }
    }
}
