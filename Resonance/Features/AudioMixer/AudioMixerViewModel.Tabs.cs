using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Components;
using Resonance.Services;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace Resonance.Features.AudioMixer
{
    // Présentation du mixeur en deux familles de canaux : les ambiances et musiques (qui tournent,
    // affichées en tranches de console) et les effets (joués une fois, affichés en pads). Sur téléphone, un
    // onglet par famille ; sur Windows, les deux côte à côte (IsSideBySide). CurrentChannels reste
    // la seule source de vérité (ordre, sauvegarde, raccourcis clavier) : AmbienceChannels et
    // EffectChannels n'en sont que des vues filtrées, tenues à jour ici.
    public partial class AudioMixerViewModel
    {
        public ObservableCollection<ChannelStripViewModel> AmbienceChannels { get; } = new();
        public ObservableCollection<ChannelStripViewModel> EffectChannels { get; } = new();

        // Canaux dont on suit IsOneShot (famille) et IsPlaying (repère sur l'onglet caché) : gardés
        // à part parce qu'un CurrentChannels.Clear() (Reset) ne fournit pas les éléments retirés.
        private readonly HashSet<ChannelStripViewModel> _watchedChannels = new();

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowAmbiences), nameof(ShowEffects), nameof(ShowAmbiencePlayingDot), nameof(ShowEffectPlayingDot))]
        private bool isEffectsTab;

        // Fixé par la page selon l'appareil : sur Windows, assez de place pour tout afficher.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowTabs), nameof(ShowAmbiences), nameof(ShowEffects))]
        private bool isSideBySide;

        public bool ShowTabs => !IsSideBySide;
        public bool ShowAmbiences => IsSideBySide || !IsEffectsTab;
        public bool ShowEffects => IsSideBySide || IsEffectsTab;

        // Repère sur l'onglet non affiché : on ne cache jamais ce qui joue. Sur l'onglet affiché, il
        // ne dirait rien de plus que les canaux eux-mêmes.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowAmbiencePlayingDot))]
        private bool isAnyAmbiencePlaying;

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(ShowEffectPlayingDot))]
        private bool isAnyEffectPlaying;

        public bool ShowAmbiencePlayingDot => IsAnyAmbiencePlaying && IsEffectsTab;
        public bool ShowEffectPlayingDot => IsAnyEffectPlaying && !IsEffectsTab;

        // Canal dont le panneau d'actions (arrêter, changer de son, réglages, retirer) est ouvert :
        // ces actions vivent une seule fois en bas de l'écran au lieu d'être répétées sur chaque canal.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(HasSelectedChannel))]
        private ChannelStripViewModel? selectedChannel;

        public bool HasSelectedChannel => SelectedChannel != null;

        partial void OnSelectedChannelChanged(ChannelStripViewModel? oldValue, ChannelStripViewModel? newValue)
        {
            if (oldValue != null) oldValue.IsSelected = false;
            if (newValue != null) newValue.IsSelected = true;
        }

        [RelayCommand]
        private void ShowAmbiencesTab() => SwitchTab(effects: false);

        [RelayCommand]
        private void ShowEffectsTab() => SwitchTab(effects: true);

        // Le panneau d'actions se referme s'il concerne un canal de l'onglet qu'on quitte : il
        // parlerait d'un canal qu'on ne voit plus.
        private void SwitchTab(bool effects)
        {
            IsEffectsTab = effects;
            if (SelectedChannel != null && SelectedChannel.IsOneShot != effects)
                SelectedChannel = null;
        }

        [RelayCommand]
        private void SelectChannel(ChannelStripViewModel? channel) =>
            SelectedChannel = channel == null || ReferenceEquals(SelectedChannel, channel) ? null : channel;

        [RelayCommand]
        private void CloseChannelPanel() => SelectedChannel = null;

        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task AddAmbience() => AddChannelAsync(isEffect: false);

        [RelayCommand(AllowConcurrentExecutions = true)]
        private Task AddEffect() => AddChannelAsync(isEffect: true);

        /// <summary>
        /// Ajoute un canal et ouvre tout de suite la Bibliothèque pour lui choisir son son : plus de
        /// canal vide à remplir ensuite. Une ambiance tourne en boucle, un effet joue une fois. Si le
        /// choix est annulé, le canal disparaît.
        /// </summary>
        private async Task AddChannelAsync(bool isEffect)
        {
            if (!HasActiveScene || _isStripDialogOpen)
                return;

            var channel = new ChannelStripViewModel
            {
                DisplayTrackName = LocalizationService.Instance["ChannelNew"] + " " + (CurrentChannels.Count + 1),
                IsLooping = !isEffect,
                IsOneShot = isEffect,
                MasterVolume = MasterVolume
            };
            CurrentChannels.Add(channel);
            SelectedChannel = channel;

            await PickLibraryItem(channel);

            if (channel.Player == null)
                CurrentChannels.Remove(channel);
        }

        /// <summary>
        /// Avant la visite guidée : premier onglet, et le panneau d'actions ouvert sur la première
        /// ambiance, pour que les étapes qui montrent ses boutons trouvent leur cible.
        /// </summary>
        public void PrepareForTour()
        {
            IsEffectsTab = false;
            SelectedChannel = AmbienceChannels.FirstOrDefault() ?? CurrentChannels.FirstOrDefault();
        }

        private void OnCurrentChannelsChanged(object? sender, NotifyCollectionChangedEventArgs e) => SyncChannelGroups();

        private void OnWatchedChannelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(ChannelStripViewModel.IsOneShot))
            {
                SyncChannelGroups();
                // Le canal change de famille depuis ses réglages : on le suit dans son nouvel onglet.
                if (sender is ChannelStripViewModel channel && ReferenceEquals(channel, SelectedChannel))
                    IsEffectsTab = channel.IsOneShot;
            }
            else if (e.PropertyName == nameof(ChannelStripViewModel.IsPlaying))
            {
                RefreshPlayingFlags();
            }
        }

        private void SyncChannelGroups()
        {
            foreach (var gone in _watchedChannels.Where(c => !CurrentChannels.Contains(c)).ToList())
            {
                gone.PropertyChanged -= OnWatchedChannelChanged;
                _watchedChannels.Remove(gone);
            }
            foreach (var added in CurrentChannels.Where(c => !_watchedChannels.Contains(c)))
            {
                added.PropertyChanged += OnWatchedChannelChanged;
                _watchedChannels.Add(added);
            }

            Patch(AmbienceChannels, CurrentChannels.Where(c => !c.IsOneShot).ToList());
            Patch(EffectChannels, CurrentChannels.Where(c => c.IsOneShot).ToList());

            if (SelectedChannel != null && !CurrentChannels.Contains(SelectedChannel))
                SelectedChannel = null;

            RefreshPlayingFlags();
        }

        private void RefreshPlayingFlags()
        {
            IsAnyAmbiencePlaying = AmbienceChannels.Any(c => c.IsPlaying);
            IsAnyEffectPlaying = EffectChannels.Any(c => c.IsPlaying);
        }

        // Met la liste affichée en conformité avec l'ordre voulu par de petits mouvements (retrait,
        // insertion, déplacement) plutôt qu'un Clear + rajout complet : la liste ne clignote pas et
        // garde son défilement quand un seul canal arrive, part ou change de place.
        private static void Patch(ObservableCollection<ChannelStripViewModel> target, List<ChannelStripViewModel> desired)
        {
            for (int i = target.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(target[i]))
                    target.RemoveAt(i);
            }

            for (int i = 0; i < desired.Count; i++)
            {
                if (i < target.Count && ReferenceEquals(target[i], desired[i]))
                    continue;

                int current = target.IndexOf(desired[i]);
                if (current >= 0)
                    target.Move(current, i);
                else
                    target.Insert(i, desired[i]);
            }
        }
    }
}
