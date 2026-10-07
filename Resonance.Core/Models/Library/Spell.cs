using CommunityToolkit.Mvvm.ComponentModel;

namespace Resonance.Models.Library
{
    public partial class Spell : LibraryItem
    {
        [ObservableProperty]
        private string description = "";
    }
}
