
using Plugin.Maui.Audio;

namespace Resonance.Services
{
    public class AudioMixerService
    {
        readonly IAudioManager audioManager;

        public AudioMixerService(IAudioManager audioManager)
        {
            this.audioManager = audioManager;
        }

        public Task<IAudioPlayer> CreatePlayerAsync(string filePath)
        {
            // Création sortie du thread UI car le lecteur natif peut être coûteux à construire.
#if WINDOWS
            // Sur Windows, CreatePlayer(string) du plugin préfixe le chemin par "ms-appx:///Assets/"
            // (réservé aux assets packagés de l'appli) : un chemin absolu devient une source
            // invalide → MediaFailed, IsPlaying toujours false, aucun son. On passe donc par un
            // Stream - mais AudioPlayer.windows.cs (Plugin.Maui.Audio) ne sait cloner que le cas
            // MemoryStream (converti en InMemoryRandomAccessStream, natif WinRT) ; tout autre
            // Stream, dont un FileStream, est enveloppé via .AsRandomAccessStream(), qui ne
            // supporte pas CloneStream et fait planter MediaSource.CreateFromStream
            // (NotSupportedException). La copie intégrale en mémoire n'est donc pas un choix ici,
            // c'est le seul chemin que le plugin gère correctement côté Windows.
            return Task.Run(async () =>
            {
                var bytes = await File.ReadAllBytesAsync(filePath);
                var stream = new MemoryStream(bytes);
                try
                {
                    return (IAudioPlayer)new StreamOwningAudioPlayer(audioManager.CreatePlayer(stream), stream);
                }
                catch
                {
                    stream.Dispose();
                    throw;
                }
            });
#else
            // Android/iOS résolvent correctement un chemin de fichier absolu (flux natif progressif).
            return Task.Run(() => audioManager.CreatePlayer(filePath));
#endif
        }

#if WINDOWS
        /// <summary>
        /// Enveloppe un player créé depuis un FileStream pour refermer ce dernier au Dispose :
        /// le plugin ne prend pas la propriété du stream source, qui resterait sinon ouvert
        /// (fichier verrouillé) jusqu'au passage du GC.
        /// </summary>
        private sealed class StreamOwningAudioPlayer(IAudioPlayer inner, Stream stream) : IAudioPlayer
        {
            public event EventHandler? PlaybackEnded
            {
                add => inner.PlaybackEnded += value;
                remove => inner.PlaybackEnded -= value;
            }

            public event EventHandler? Error
            {
                add => inner.Error += value;
                remove => inner.Error -= value;
            }

            public double Duration => inner.Duration;
            public double CurrentPosition => inner.CurrentPosition;
            public double Volume { get => inner.Volume; set => inner.Volume = value; }
            public double Balance { get => inner.Balance; set => inner.Balance = value; }
            public double Speed { get => inner.Speed; set => inner.Speed = value; }
            public double MinimumSpeed => inner.MinimumSpeed;
            public double MaximumSpeed => inner.MaximumSpeed;
            public bool CanSetSpeed => inner.CanSetSpeed;
            public bool CanSeek => inner.CanSeek;
            public bool IsPlaying => inner.IsPlaying;
            public bool Loop { get => inner.Loop; set => inner.Loop = value; }

            public void Play() => inner.Play();
            public void Pause() => inner.Pause();
            public void Stop() => inner.Stop();
            public void Seek(double position) => inner.Seek(position);
            public void SetSource(Stream source) => inner.SetSource(source);

            public void Dispose()
            {
                inner.Dispose();
                stream.Dispose();
            }
        }
#endif
    }
}
