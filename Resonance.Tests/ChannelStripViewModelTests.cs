using Resonance.Components;
using Plugin.Maui.Audio;

namespace Resonance.Tests;

public class ChannelStripViewModelTests : IDisposable
{
    private readonly TimeSpan _originalDuration;
    private readonly int _originalSteps;

    public ChannelStripViewModelTests()
    {
        // Fades raccourcis pour les tests (60 ms / 3 pas au lieu de 1,5 s / 30 pas).
        _originalDuration = ChannelStripViewModel.FadeDuration;
        _originalSteps = ChannelStripViewModel.FadeSteps;
        ChannelStripViewModel.FadeDuration = TimeSpan.FromMilliseconds(60);
        ChannelStripViewModel.FadeSteps = 3;
    }

    public void Dispose()
    {
        ChannelStripViewModel.FadeDuration = _originalDuration;
        ChannelStripViewModel.FadeSteps = _originalSteps;
    }

    private static (ChannelStripViewModel vm, FakeAudioPlayer player) MakeStrip(double volume = 0.8)
    {
        var player = new FakeAudioPlayer();
        var vm = new ChannelStripViewModel { Volume = volume, Player = player };
        return (vm, player);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
            await Task.Delay(10);
        Assert.True(condition());
    }

    [Fact]
    public void Play_WithoutFadeIn_PlaysAtTargetVolume()
    {
        var (vm, player) = MakeStrip(volume: 0.8);

        vm.Play();

        Assert.True(vm.IsPlaying);
        Assert.True(player.IsPlaying);
        Assert.Equal(0.8, player.Volume);
    }

    [Fact]
    public async Task Play_WithFadeIn_StartsSilent_ThenReachesTargetVolume()
    {
        var (vm, player) = MakeStrip(volume: 0.8);
        vm.IsFadeIn = true;

        vm.Play();

        // La lecture démarre immédiatement, mais à volume nul.
        Assert.True(vm.IsPlaying);
        Assert.True(player.IsPlaying);
        Assert.Equal(0, player.Volume);

        await WaitUntilAsync(() => Math.Abs(player.Volume - 0.8) < 0.001);
    }

    [Fact]
    public async Task FadeOut_StopsPlayer_AndRestoresVolumeForNextPlay()
    {
        var (vm, player) = MakeStrip(volume: 0.6);
        vm.Play();

        await vm.FadeOut();

        Assert.False(vm.IsPlaying);
        Assert.Contains("Stop", player.Calls);
        Assert.Equal(0.6, player.Volume);
    }

    [Fact]
    public async Task StopPlayback_WithoutFadeOutSetting_StopsImmediately()
    {
        var (vm, player) = MakeStrip();
        vm.Play();
        player.Calls.Clear();

        await vm.StopPlayback();

        Assert.Equal(new[] { "Stop" }, player.Calls);
        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public async Task StopPlayback_WithFadeOutSetting_FadesBeforeStopping()
    {
        var (vm, player) = MakeStrip(volume: 1.0);
        vm.IsFadeOut = true;
        vm.Play();
        player.VolumeHistory.Clear();

        await vm.StopPlayback();

        Assert.False(vm.IsPlaying);
        Assert.Contains("Stop", player.Calls);
        // Le volume est descendu par paliers avant l'arrêt (au moins un palier intermédiaire < 1).
        Assert.Contains(player.VolumeHistory, v => v < 1.0);
    }

    [Fact]
    public void PlaybackEnded_ResetsStripState_AndRewinds()
    {
        var (vm, player) = MakeStrip();
        vm.Play();

        player.RaisePlaybackEnded(stillPlaying: false);

        Assert.False(vm.IsPlaying);
        Assert.Equal(0, player.LastSeek);
    }

    [Fact]
    public void PlaybackEnded_WhileLooping_KeepsPlayingState()
    {
        var (vm, player) = MakeStrip();
        vm.IsLooping = true;
        vm.Play();

        // Certaines plateformes notifient la fin de chaque itération de boucle alors que la
        // lecture continue : l'état du strip ne doit pas être réinitialisé.
        player.RaisePlaybackEnded(stillPlaying: true);

        Assert.True(vm.IsPlaying);
        Assert.Null(player.LastSeek);
    }

    [Fact]
    public void TogglePlay_PausesWhenPlaying_AndResumes()
    {
        var (vm, player) = MakeStrip();

        vm.TogglePlay();
        Assert.True(vm.IsPlaying);

        vm.TogglePlay();
        Assert.False(vm.IsPlaying);
        Assert.Contains("Pause", player.Calls);
    }

    [Fact]
    public void VolumeAndLoopChanges_ApplyLiveToPlayer()
    {
        var (vm, player) = MakeStrip();

        vm.Volume = 0.25;
        vm.IsLooping = true;

        Assert.Equal(0.25, player.Volume);
        Assert.True(player.Loop);
    }

    [Fact]
    public void ReplacingPlayer_DetachesOldPlaybackEndedHandler()
    {
        var (vm, oldPlayer) = MakeStrip();
        vm.Play();

        vm.Player = new FakeAudioPlayer();

        // L'ancien player qui se termine ne doit plus toucher l'état du strip.
        vm.IsPlaying = true;
        oldPlayer.RaisePlaybackEnded(stillPlaying: false);
        Assert.True(vm.IsPlaying);
    }

    [Fact]
    public void ReplacingPlayer_DisposesOldPlayer()
    {
        var (vm, oldPlayer) = MakeStrip();

        vm.Player = new FakeAudioPlayer();

        // Un player remplacé ne sera plus jamais utilisé : sans Dispose, le FileStream sous-jacent
        // reste ouvert sur Windows et le MediaPlayer natif fuit sur Android.
        Assert.Contains("Dispose", oldPlayer.Calls);
    }

    [Fact]
    public void DisposePlayer_DisposesPlayer_AndResetsState()
    {
        var (vm, player) = MakeStrip();
        vm.Play();

        vm.DisposePlayer();

        Assert.Null(vm.Player);
        Assert.False(vm.IsPlaying);
        Assert.Contains("Dispose", player.Calls);
    }

    // --- Tests ajoutés pour les mutants survivants de Stryker (cf. Resonance.Tests/stryker-config.json) ---

    private static void AssertVolumes(IEnumerable<double> expected, IEnumerable<double> actual) =>
        Assert.Equal(expected.Select(v => Math.Round(v, 6)), actual.Select(v => Math.Round(v, 6)));

    [Fact]
    public void SettingSamePlayerAgain_KeepsIt()
    {
        var (vm, player) = MakeStrip();

        vm.Player = player;

        Assert.DoesNotContain("Dispose", player.Calls);
        Assert.True(player.HasPlaybackEndedSubscribers);
    }

    [Fact]
    public void ReplacingPlayer_UnsubscribesFromOldPlayer()
    {
        var (vm, oldPlayer) = MakeStrip();

        vm.Player = new FakeAudioPlayer();

        Assert.False(oldPlayer.HasPlaybackEndedSubscribers);
    }

    [Fact]
    public async Task ReplacingPlayer_CancelsRunningFade_SoTheNewPlayerIsNotStopped()
    {
        var (vm, oldPlayer) = MakeStrip();
        vm.Play();
        var fade = vm.FadeOut();

        var newPlayer = new FakeAudioPlayer();
        vm.Player = newPlayer;
        await fade;

        Assert.DoesNotContain("Stop", newPlayer.Calls);
    }

    [Fact]
    public void PlaybackEnded_DispatchedAfterPlayerWasReplaced_IsIgnored()
    {
        // Fin de lecture notifiée par l'ancien player, mais traitée par le dispatcher UI après son
        // remplacement : elle ne doit pas toucher au nouveau player ni à l'état du strip.
        var queued = new List<Action>();
        var originalDispatcher = ChannelStripViewModel.UiDispatcher;
        ChannelStripViewModel.UiDispatcher = queued.Add;
        try
        {
            var (vm, oldPlayer) = MakeStrip();
            vm.Play();
            oldPlayer.RaisePlaybackEnded(stillPlaying: false);

            var newPlayer = new FakeAudioPlayer();
            vm.Player = newPlayer;
            vm.IsPlaying = true;
            queued.ForEach(a => a());

            Assert.True(vm.IsPlaying);
            Assert.Null(newPlayer.LastSeek);
        }
        finally
        {
            ChannelStripViewModel.UiDispatcher = originalDispatcher;
        }
    }

    [Fact]
    public void DisplayVolume_IsVolumeAsPercent()
    {
        var (vm, _) = MakeStrip(volume: 0.5);

        Assert.Equal(50, vm.DisplayVolume);
    }

    [Fact]
    public void RefreshThemeDependentBindings_NotifiesPlayingAndFadeSettings()
    {
        var (vm, _) = MakeStrip();
        var notified = new List<string?>();
        vm.PropertyChanged += (_, e) => notified.Add(e.PropertyName);

        vm.RefreshThemeDependentBindings();

        Assert.Equal(new[] { nameof(vm.IsPlaying), nameof(vm.IsFadeIn), nameof(vm.IsFadeOut) }, notified);
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(42, true)]
    public void HasSceneTrack_OnlyForPersistedSceneTracks(int sceneTrackId, bool expected)
    {
        var (vm, _) = MakeStrip();

        vm.SceneTrackId = sceneTrackId;

        Assert.Equal(expected, vm.HasSceneTrack);
    }

    [Fact]
    public void VolumeChange_IsSavedOnTheTrack_AndLiveVolume()
    {
        var (vm, _) = MakeStrip();

        vm.Volume = 0.3;

        Assert.Equal(0.3, vm.Track!.Volume);
        Assert.Equal(0.3, vm.LiveVolume);
    }

    [Fact]
    public void VolumeChange_WithoutTrack_DoesNotThrow()
    {
        var (vm, player) = MakeStrip();
        vm.Track = null;

        vm.Volume = 0.3;

        Assert.Equal(0.3, player.Volume);
    }

    [Fact]
    public async Task Commands_WithoutPlayer_DoNothing()
    {
        var vm = new ChannelStripViewModel();

        vm.TogglePlay();
        vm.Play();
        vm.Pause();
        vm.Stop();
        await vm.FadeOut();

        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public void Play_WhileAlreadyPlaying_DoesNotRestartThePlayer()
    {
        var (vm, player) = MakeStrip();

        vm.Play();
        vm.Play();

        Assert.Single(player.Calls, c => c == "Play");
    }

    [Fact]
    public void Pause_PausesThePlayer_AndClearsPlayingState()
    {
        var (vm, player) = MakeStrip();
        vm.Play();

        vm.Pause();

        Assert.False(vm.IsPlaying);
        Assert.Contains("Pause", player.Calls);
    }

    [Fact]
    public async Task FadeIn_RampsVolumeByEqualSteps()
    {
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.IsFadeIn = true;
        player.VolumeHistory.Clear();

        vm.Play();
        await WaitUntilAsync(() => player.VolumeHistory.Count >= 5);
        await Task.Delay(50);

        // 0 au démarrage, 3 paliers, puis la valeur cible réappliquée en fin de fade.
        AssertVolumes(new[] { 0, 0.3, 0.6, 0.9, 0.9 }, player.VolumeHistory);
        Assert.Equal(0.9, vm.LiveVolume, 6);
    }

    [Fact]
    public async Task Stop_DuringFadeIn_CancelsIt_AndRestoresTargetVolume()
    {
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.IsFadeIn = true;
        vm.Play();
        await WaitUntilAsync(() => player.Volume > 0);

        vm.Stop();
        var afterStop = player.VolumeHistory.Count;
        await WaitUntilAsync(() => Math.Abs(player.Volume - 0.9) < 0.001);
        await Task.Delay(80);

        // Plus aucun palier après l'arrêt : seulement la valeur cible, remise d'un coup.
        Assert.All(player.VolumeHistory.Skip(afterStop), v => Assert.Equal(0.9, v, 6));
        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public async Task FadeIn_WhenPlayerFails_LeavesTheStripStopped()
    {
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.IsFadeIn = true;
        player.ThrowOnVolume = v => v > 0;

        vm.Play();

        await WaitUntilAsync(() => !vm.IsPlaying);
    }

    [Fact]
    public async Task Stop_AfterACompletedFade_DoesNotThrow()
    {
        // Un fade terminé libère son CancellationTokenSource : le strip ne doit plus le garder,
        // sinon le prochain arrêt annulerait un objet déjà libéré.
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.IsFadeIn = true;
        vm.Play();
        await WaitUntilAsync(() => player.VolumeHistory.Count >= 5);
        await Task.Delay(50);

        vm.Stop();
        vm.IsFadeIn = false;
        vm.Play();
        await vm.FadeOut();
        vm.Stop();

        Assert.False(vm.IsPlaying);
    }

    [Fact]
    public async Task FadeOut_RampsVolumeDownByEqualSteps_ThenRestoresIt()
    {
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.Play();
        player.VolumeHistory.Clear();

        await vm.FadeOut();

        AssertVolumes(new[] { 0.6, 0.3, 0, 0.9 }, player.VolumeHistory);
        Assert.Equal(0.9, vm.LiveVolume, 6);
    }

    [Fact]
    public async Task FadeOut_TakesTheConfiguredDuration()
    {
        var (vm, _) = MakeStrip();
        vm.Play();
        var watch = System.Diagnostics.Stopwatch.StartNew();

        await vm.FadeOut();

        // 3 paliers de 20 ms : ni instantané, ni 9 fois plus long (pas mal calculé).
        Assert.InRange(watch.ElapsedMilliseconds, 40, 400);
    }

    [Fact]
    public async Task FadeIn_TakesTheConfiguredDuration()
    {
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.IsFadeIn = true;
        var watch = System.Diagnostics.Stopwatch.StartNew();

        vm.Play();
        await WaitUntilAsync(() => player.VolumeHistory.Count(v => Math.Abs(v - 0.9) < 0.001) >= 2);

        Assert.InRange(watch.ElapsedMilliseconds, 40, 400);
    }

    [Fact]
    public async Task FadeOut_WhenNotPlaying_DoesNothing()
    {
        var (vm, player) = MakeStrip(volume: 0.9);

        await vm.FadeOut();

        Assert.DoesNotContain("Stop", player.Calls);
        Assert.Equal(0.9, player.Volume);
    }

    [Fact]
    public async Task FadeOut_DuringFadeIn_CancelsTheFadeIn()
    {
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.IsFadeIn = true;
        vm.Play();
        await WaitUntilAsync(() => player.Volume > 0);

        // L'annulation du fade in remet d'abord le volume cible, puis le fade out ne fait que
        // descendre : aucune remontée tant que le player n'est pas arrêté.
        var start = player.VolumeHistory.Count;
        await vm.FadeOut();
        var duringFade = player.VolumeHistory.Skip(start).ToList();
        duringFade.RemoveAt(duringFade.Count - 1); // volume restauré après l'arrêt
        var descent = duringFade.SkipWhile(v => Math.Abs(v - 0.9) < 0.001).ToList();

        Assert.NotEmpty(descent);
        Assert.Equal(descent.OrderByDescending(v => v), descent);
    }

    [Fact]
    public async Task Stop_DuringFadeOut_RestoresVolumeForNextPlay()
    {
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.Play();
        var fade = vm.FadeOut();
        await WaitUntilAsync(() => player.Volume < 0.9);

        vm.Stop();
        await fade;

        Assert.Equal(0.9, player.Volume, 6);
        Assert.Equal(0.9, vm.LiveVolume, 6);
    }

    [Fact]
    public async Task Play_AfterPauseDuringFadeOut_CancelsTheFadeOut()
    {
        var (vm, player) = MakeStrip(volume: 0.9);
        vm.Play();
        var fade = vm.FadeOut();
        await WaitUntilAsync(() => player.Volume < 0.9);

        vm.Pause();
        vm.Play();
        await fade;

        // Le fade out interrompu ne doit pas arrêter la lecture relancée.
        Assert.True(player.IsPlaying);
        Assert.True(vm.IsPlaying);
        Assert.DoesNotContain("Stop", player.Calls);
    }
}

/// <summary>Player factice : enregistre les appels et volumes pour vérifier les fades sans audio réel.</summary>
internal sealed class FakeAudioPlayer : IAudioPlayer
{
    public event EventHandler? PlaybackEnded;
    public event EventHandler? Error;

    private double _volume = 1;

    public List<string> Calls { get; } = new();
    public List<double> VolumeHistory { get; } = new();
    public double? LastSeek { get; private set; }

    public double Duration => 100;
    public double CurrentPosition { get; private set; }
    public double Volume
    {
        get => _volume;
        set
        {
            if (ThrowOnVolume?.Invoke(value) == true)
                throw new InvalidOperationException("Player natif en échec (simulé).");
            _volume = value;
            VolumeHistory.Add(value);
        }
    }

    /// <summary>Simule une erreur du player natif pour certains volumes (cf. échec de fade in).</summary>
    public Func<double, bool>? ThrowOnVolume { get; set; }

    public bool HasPlaybackEndedSubscribers => PlaybackEnded != null;
    public double Balance { get; set; }
    public double Speed { get; set; } = 1;
    public double MinimumSpeed => 1;
    public double MaximumSpeed => 1;
    public bool CanSetSpeed => false;
    public bool CanSeek => true;
    public bool IsPlaying { get; private set; }
    public bool Loop { get; set; }

    public void Play() { IsPlaying = true; Calls.Add("Play"); }
    public void Pause() { IsPlaying = false; Calls.Add("Pause"); }
    public void Stop() { IsPlaying = false; Calls.Add("Stop"); }
    public void Seek(double position) { CurrentPosition = position; LastSeek = position; Calls.Add("Seek"); }
    public void SetSource(Stream stream) { }
    public void Dispose() => Calls.Add("Dispose");

    public void RaisePlaybackEnded(bool stillPlaying)
    {
        IsPlaying = stillPlaying;
        PlaybackEnded?.Invoke(this, EventArgs.Empty);
    }

    // Évite l'avertissement "événement jamais utilisé" — le contrat IAudioPlayer l'impose.
    public void RaiseError() => Error?.Invoke(this, EventArgs.Empty);
}
