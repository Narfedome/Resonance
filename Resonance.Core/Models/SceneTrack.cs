using CommunityToolkit.Mvvm.ComponentModel;
using Resonance.Models.Library;

namespace Resonance.Models
{
    public partial class SceneTrack : ObservableObject
    {
        public int Id { get; set; }

        public int SceneId { get; set; }

        public int Position { get; set; }

        [ObservableProperty]
        private Track track = new();

        [ObservableProperty]
        private double volume = 1.0;

        [ObservableProperty]
        private bool autoPlay = false;

        [ObservableProperty]
        private bool isLooping = true;

        [ObservableProperty]
        private bool fadeIn = false;

        [ObservableProperty]
        private bool fadeOut = false;
    }
}
