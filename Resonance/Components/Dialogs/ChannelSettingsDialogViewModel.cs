using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Resonance.Components.Dialogs
{
    /// <summary>
    /// Logique du dialogue de réglages d'un channel strip (ChannelSettingsDialog n'est qu'un
    /// wrapper XAML lié dessus). Ne persiste rien lui-même : l'appelant (AudioMixerViewModel) lit
    /// les propriétés éditées une fois le dialogue confirmé et se charge d'appliquer/sauvegarder.
    /// </summary>
    public partial class ChannelSettingsDialogViewModel : DialogViewModel<bool>
    {
        protected override bool CancelResult => false;

        [ObservableProperty]
        private string trackName = string.Empty;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(VolumePercent))]
        private double volume;

        public int VolumePercent => (int)(Volume * 100);

        [ObservableProperty]
        private bool isLooping;

        [ObservableProperty]
        private bool fadeIn;

        [ObservableProperty]
        private bool fadeOut;

        [ObservableProperty]
        private bool autoPlay;

        public ChannelSettingsDialogViewModel(string trackName, double volume, bool isLooping, bool fadeIn, bool fadeOut, bool autoPlay)
        {
            TrackName = trackName;
            Volume = volume;
            IsLooping = isLooping;
            FadeIn = fadeIn;
            FadeOut = fadeOut;
            AutoPlay = autoPlay;
        }

        [RelayCommand]
        public void Save() => Close(true);
    }
}
