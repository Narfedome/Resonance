using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace Resonance.Models
{
    public partial class Scene : ObservableObject
    {
        public int Id { get; set; }

        public int SessionId { get; set; }

        public int Position { get; set; }

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private ObservableCollection<SceneTrack> tracks = new();
    }
}
