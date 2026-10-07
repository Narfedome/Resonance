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
        set { _volume = value; VolumeHistory.Add(value); }
    }
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
