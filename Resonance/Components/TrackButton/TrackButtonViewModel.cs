using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Models.Library;
using Resonance.Services;
using System.ComponentModel;

namespace Resonance.Components.TrackButton
{
    public partial class TrackButtonViewModel : BaseViewModel
    {
        private readonly AudioPlayerService _audioService;
        private readonly CoverArtService _coverArtService;

        public TrackButtonViewModel(AudioPlayerService audioService, CoverArtService coverArtService)
        {
            _audioService = audioService;
            _coverArtService = coverArtService;

            _audioService.OnStateChanged += OnAudioChanged;
        }

        /// <summary>
        /// Réabonne le ViewModel quand sa vue revient à l'écran (recyclage) et resynchronise
        /// l'état de lecture manqué pendant la déconnexion.
        /// </summary>
        public void Attach()
        {
            _audioService.OnStateChanged -= OnAudioChanged;
            _audioService.OnStateChanged += OnAudioChanged;

            if (CurrentTrack != null)
            {
                CurrentTrack.PropertyChanged -= OnTrackChanged;
                CurrentTrack.PropertyChanged += OnTrackChanged;
            }

            UpdateFromTrack(CurrentTrack);
        }

        /// <summary>
        /// Désabonne le ViewModel du service audio (singleton) et de la track quand sa vue quitte
        /// l'écran : sans ça, chaque tuile jamais créée restait accrochée au service à vie
        /// (fuite mémoire croissante au fil des rechargements de la bibliothèque).
        /// </summary>
        public void Detach()
        {
            _audioService.OnStateChanged -= OnAudioChanged;

            if (CurrentTrack != null)
                CurrentTrack.PropertyChanged -= OnTrackChanged;
        }

        private void OnAudioChanged(string? currentFile)
        {
            IsPlaying = CurrentTrack != null && currentFile == CurrentTrack.FilePath;
        }

        [ObservableProperty]
        private Track? currentTrack;
        partial void OnCurrentTrackChanged(Track? oldValue, Track? newValue)
        {
            if (oldValue != null)
                oldValue.PropertyChanged -= OnTrackChanged;

            if (newValue != null)
                newValue.PropertyChanged += OnTrackChanged;

            UpdateFromTrack(newValue);
        }
        private void OnTrackChanged(object? sender, PropertyChangedEventArgs e)
        {
            UpdateFromTrack(CurrentTrack);
        }

        private void UpdateFromTrack(Track? value)
        {
            if (value == null)
            {
                CoverImage = null;
                IsPlaying = false;
                IsLoadingCover = false;
                return;
            }

            IsPlaying = _audioService.CurrentFile == value.FilePath;

            if (_coverArtService.TryGetCached(value, out var cached))
            {
                CoverImage = cached;
                IsLoadingCover = false;
                return;
            }

            CoverImage = null;
            IsLoadingCover = true;
            _ = LoadCoverImageAsync(value);
        }

        private async Task LoadCoverImageAsync(Track track)
        {
            var image = await _coverArtService.GetCoverAsync(track);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                if (CurrentTrack == track)
                {
                    CoverImage = image;
                    IsLoadingCover = false;
                }
            });
        }

        [ObservableProperty]
        private bool isPlaying;

        [ObservableProperty]
        private bool isLoadingCover;

        [ObservableProperty]
        private ImageSource? coverImage;

        [RelayCommand]
        private async Task TogglePlay()
        {
            if (CurrentTrack == null)
                return;

            try
            {
                _audioService.Toggle(CurrentTrack.FilePath);
            }
            catch (Exception ex)
            {
                // Fichier manquant ou illisible : message plutôt que crash de l'appli.
                await ShowErrorAsync(ex);
            }
        }
    }
}
