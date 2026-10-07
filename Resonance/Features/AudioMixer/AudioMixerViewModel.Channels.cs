using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Components;
using Resonance.Components.Dialogs;
using Resonance.Features.Library;
using Resonance.Models;
using Resonance.Models.Library;
using Resonance.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace Resonance.Features.AudioMixer
{
    // CRUD des channel strips, cycle de vie des players et sauvegarde (immédiate ou debouncée) en
    // SceneTrack. Complète la partie AudioMixerViewModel.cs (sélection/navigation de scène), avec
    // laquelle elle partage CurrentChannels et _activeScene.
    public partial class AudioMixerViewModel
    {
        private readonly Dictionary<ChannelStripViewModel, CancellationTokenSource> _pendingSaves = new();

        [ObservableProperty]
        private ObservableCollection<ChannelStripViewModel> currentChannels = new();

        [RelayCommand]
        public void AddChannel()
        {
            var channel = new ChannelStripViewModel() { DisplayTrackName = (LocalizationService.Instance["ChannelNew"] + " " + (CurrentChannels.Count + 1)), IsPlaying = false };
            CurrentChannels.Add(channel);
        }

        [RelayCommand]
        public void PlayAll()
        {
            foreach (var c in CurrentChannels)
                c.Play();
        }

        [RelayCommand]
        public void StopAll()
        {
            foreach (var c in CurrentChannels)
                c.Stop();
        }

        [RelayCommand]
        public async Task FadeOutAll()
        {
            var tasks = CurrentChannels.Select(c => c.FadeOut()).ToArray();
            await Task.WhenAll(tasks);
        }

        // Les boutons livre/gear/X de chaque strip sont liés à UNE seule instance de commande du
        // ViewModel (RelativeSource dans le template) : sans AllowConcurrentExecutions, le toolkit
        // passe CanExecute à false pendant toute l'exécution async, et TOUS les boutons liés
        // passent visuellement en état désactivé tant que le dialog est ouvert. Ce garde reprend
        // le seul rôle utile de ce blocage : empêcher un double-tap d'ouvrir deux dialogs.
        private bool _isStripDialogOpen;

        [RelayCommand(AllowConcurrentExecutions = true)]
        public async Task RemoveChannel(ChannelStripViewModel channel)
        {
            if (channel == null || _isStripDialogOpen)
                return;
            if (channel.Player == null)
            {
                UnsubscribeChannel(channel);
                if (channel.SceneTrackId > 0)
                    await _sceneDataService.DeleteSceneTrackAsync(new SceneTrack { Id = channel.SceneTrackId });
                CurrentChannels.Remove(channel);
                return;
            }

            _isStripDialogOpen = true;
            try
            {
                // La lecture est suspendue le temps du dialogue, puis reprise UNIQUEMENT si elle
                // était en cours : un TogglePlay inconditionnel lançait la lecture d'un strip à
                // l'arrêt quand l'utilisateur annulait la suppression.
                var wasPlaying = channel.IsPlaying;
                channel.Pause();

                bool confirm = await ConfirmAsync(Loc["DialogDelete"], string.Format(Loc["DialogRemoveChannel"], channel.DisplayTrackName));

                if (!confirm)
                {
                    if (wasPlaying)
                        channel.Play();
                    return;
                }

                channel.Stop();
                UnsubscribeChannel(channel);
                channel.DisposePlayer();
                if (channel.SceneTrackId > 0)
                    await _sceneDataService.DeleteSceneTrackAsync(new SceneTrack { Id = channel.SceneTrackId });
                CurrentChannels.Remove(channel);
            }
            finally
            {
                _isStripDialogOpen = false;
            }
        }

        [RelayCommand(AllowConcurrentExecutions = true)]
        public async Task PickLibraryItem(ChannelStripViewModel channel)
        {
            if (channel == null || _isStripDialogOpen) return;

            _isStripDialogOpen = true;
            try
            {
                var selectedLibraryItem = await _pickerService.PickTrackAsync();

                if (selectedLibraryItem is not Track selectedTrack || !File.Exists(selectedTrack.FilePath))
                    return;

                channel.Player = await _audioMixerService.CreatePlayerAsync(selectedTrack.FilePath);
                channel.Track = selectedTrack;
                channel.DisplayTrackName = selectedTrack.Title;

                await SaveChannelAsSceneTrack(channel);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
            finally
            {
                _isStripDialogOpen = false;
            }
        }

        /// <summary>
        /// Ouvre les paramètres de la piste assignée au channel strip dans une boîte de dialogue.
        /// Save : applique au strip (le volume agit en direct sur le player) et persiste ; Cancel
        /// (ou tap à côté) : referme sans rien toucher.
        /// </summary>
        [RelayCommand(AllowConcurrentExecutions = true)]
        public async Task OpenChannelSettings(ChannelStripViewModel channel)
        {
            if (channel == null || _activeScene == null || channel.SceneTrackId == 0 || _isStripDialogOpen)
                return;

            _isStripDialogOpen = true;
            try
            {
                // L'AutoPlay n'est pas porté par le strip : on relit l'état persisté de la piste.
                var sceneTracks = await _sceneDataService.GetSceneTracksAsync(_activeScene.Id);
                var persisted = sceneTracks.FirstOrDefault(st => st.Id == channel.SceneTrackId);
                if (persisted == null)
                    return;

                var dialogViewModel = new ChannelSettingsDialogViewModel(
                    channel.DisplayTrackName ?? persisted.Track.Title,
                    channel.Volume,
                    channel.IsLooping,
                    channel.IsFadeIn,
                    channel.IsFadeOut,
                    persisted.AutoPlay);

                var saved = await ShowDialogAsync(new ChannelSettingsDialog(dialogViewModel));
                if (!saved)
                    return;

                channel.Volume = dialogViewModel.Volume;
                channel.IsLooping = dialogViewModel.IsLooping;
                channel.IsFadeIn = dialogViewModel.FadeIn;
                channel.IsFadeOut = dialogViewModel.FadeOut;
                channel.IsAutoPlay = dialogViewModel.AutoPlay;

                // Les affectations ci-dessus viennent de déclencher une sauvegarde debouncée,
                // redondante avec la sauvegarde immédiate et complète qui suit : on l'annule.
                if (_pendingSaves.TryGetValue(channel, out var pendingCts))
                {
                    pendingCts.Cancel();
                    _pendingSaves.Remove(channel);
                }

                await _sceneDataService.UpdateSceneTrackAsync(
                    channel.SceneTrackId, dialogViewModel.Volume, dialogViewModel.IsLooping, dialogViewModel.AutoPlay, dialogViewModel.FadeIn, dialogViewModel.FadeOut);
            }
            finally
            {
                _isStripDialogOpen = false;
            }
        }

        /// <summary>
        /// Vide le mixer proprement : désabonnement des sauvegardes auto et libération des players
        /// natifs (FileStream Windows / MediaPlayer Android). Utilisé au changement de scène et
        /// avant la suppression massive de pistes (un player même en pause verrouille son fichier
        /// sur Windows).
        /// </summary>
        public void ClearChannels()
        {
            foreach (var c in CurrentChannels)
            {
                UnsubscribeChannel(c);
                c.DisposePlayer();
            }
            CurrentChannels.Clear();
        }

        // Appelée depuis le code-behind d'AudioMixerPage (gestionnaire Drop) pour réordonner les
        // channel strips par glisser-déposer. Seuls les strips déjà persistés (SceneTrackId > 0)
        // sont inclus dans la sauvegarde : un strip tout juste ajouté (aucune piste assignée) n'a
        // pas encore de SceneTrack, sa position sera fixée normalement lors de son premier
        // SaveChannelAsSceneTrack (qui lit déjà l'index courant dans CurrentChannels).
        public async Task ReorderChannelsAsync(ChannelStripViewModel dragged, ChannelStripViewModel target)
        {
            if (dragged == null || target == null || ReferenceEquals(dragged, target)) return;

            var oldIndex = CurrentChannels.IndexOf(dragged);
            var newIndex = CurrentChannels.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0) return;

            CurrentChannels.Move(oldIndex, newIndex);

            var orderedIds = CurrentChannels.Where(c => c.SceneTrackId > 0).Select(c => c.SceneTrackId).ToList();
            if (orderedIds.Count > 0)
                await _sceneDataService.ReorderSceneTracksAsync(orderedIds);
        }

        private async Task SaveChannelAsSceneTrack(ChannelStripViewModel channel)
        {
            if (_activeScene == null || channel.Track == null || channel.Track.Id == 0) return;

            var sceneTrack = new SceneTrack
            {
                Id = channel.SceneTrackId,
                SceneId = _activeScene.Id,
                Track = channel.Track,
                Volume = channel.Volume,
                IsLooping = channel.IsLooping,
                FadeIn = channel.IsFadeIn,
                FadeOut = channel.IsFadeOut,
                AutoPlay = channel.IsPlaying,
                Position = CurrentChannels.IndexOf(channel)
            };

            await _sceneDataService.SaveSceneTrackAsync(sceneTrack);
            channel.SceneTrackId = sceneTrack.Id;

            SubscribeChannel(channel);
        }

        // ── Subscriptions pour sauvegarde automatique ─────────────

        private void SubscribeChannel(ChannelStripViewModel channel)
        {
            channel.PropertyChanged -= OnChannelPropertyChanged;
            channel.PropertyChanged += OnChannelPropertyChanged;
        }

        private void UnsubscribeChannel(ChannelStripViewModel channel)
        {
            channel.PropertyChanged -= OnChannelPropertyChanged;
            if (_pendingSaves.TryGetValue(channel, out var cts))
            {
                cts.Cancel();
                _pendingSaves.Remove(channel);
            }
        }

        private void OnChannelPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (sender is not ChannelStripViewModel channel) return;
            if (e.PropertyName is nameof(ChannelStripViewModel.Volume) or nameof(ChannelStripViewModel.IsLooping))
                _ = DebouncedSaveChannel(channel);
        }

        private async Task DebouncedSaveChannel(ChannelStripViewModel channel)
        {
            if (channel.SceneTrackId == 0) return;

            if (_pendingSaves.TryGetValue(channel, out var existingCts))
                existingCts.Cancel();

            var cts = new CancellationTokenSource();
            _pendingSaves[channel] = cts;

            try
            {
                await Task.Delay(500, cts.Token);
                // Sauvegarde partielle : l'AutoPlay est un réglage explicite de l'utilisateur
                // (dialog de paramètres / page des pistes de scène), il ne doit pas être écrasé
                // par l'état de lecture du moment à chaque changement de volume ou de boucle.
                await _sceneDataService.UpdateSceneTrackSettingsAsync(
                    channel.SceneTrackId, channel.Volume, channel.IsLooping, channel.IsFadeIn, channel.IsFadeOut);
            }
            catch (OperationCanceledException) { }
            finally
            {
                if (_pendingSaves.TryGetValue(channel, out var current) && current == cts)
                    _pendingSaves.Remove(channel);
                cts.Dispose();
            }
        }

        /// <summary>
        /// Ajoute tout de suite un strip par piste jouable, dans l'ordre de la scène, avec
        /// IsLoading à true (cf. binding dans AudioMixerPage.xaml qui désactive le strip et
        /// affiche un spinner local) puis crée les lecteurs en parallèle - chaque strip bascule à
        /// IsLoading=false dès que SON lecteur est prêt, indépendamment des autres. Volontairement
        /// hors de Loading.RunAsync : sinon l'overlay plein écran resterait affiché jusqu'à ce que
        /// TOUS les lecteurs soient créés, masquant cet affichage progressif.
        /// </summary>
        private async Task PopulateChannelsAsync(List<SceneTrack> playable)
        {
            var channels = playable.Select(st => new ChannelStripViewModel
            {
                SceneTrackId = st.Id,
                Track = st.Track,
                DisplayTrackName = st.Track.Title,
                Volume = st.Volume,
                IsLooping = st.IsLooping,
                IsFadeIn = st.FadeIn,
                IsFadeOut = st.FadeOut,
                IsAutoPlay = st.AutoPlay,
                IsLoading = true
            }).ToList();

            foreach (var channel in channels)
                CurrentChannels.Add(channel);

            // Echecs collectes plutot que remontes un par un : plusieurs strips se chargent en
            // parallele (Task.WhenAll), un ShowErrorAsync par echec empilerait plusieurs popups
            // modales d'un coup - une seule notification groupee a la fin est plus lisible.
            var failedTracks = new List<string>();
            var creationTasks = playable.Zip(channels, async (st, channel) =>
            {
                try
                {
                    channel.Player = await _audioMixerService.CreatePlayerAsync(st.Track.FilePath);
                    SubscribeChannel(channel);

                    if (st.AutoPlay)
                        channel.Play();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[AudioMixerViewModel] Échec de création du lecteur pour '{st.Track.Title}' : {ex}");
                    failedTracks.Add(st.Track.Title);
                    CurrentChannels.Remove(channel);
                }
                finally
                {
                    channel.IsLoading = false;
                }
            });
            await Task.WhenAll(creationTasks);

            if (failedTracks.Count > 0)
                await ShowInfoAsync(Loc["ErrorTitle"], string.Format(Loc["ErrorTracksFailedToLoad"], string.Join(", ", failedTracks)));
        }
    }
}
