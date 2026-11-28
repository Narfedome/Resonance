namespace Resonance.Components;

public partial class MixerEffectPadView : ContentView
{
    public MixerEffectPadView()
    {
        InitializeComponent();
    }

    private void OnDragStarting(object? sender, DragStartingEventArgs e) => ChannelDrag.Start(this, e);

    private void OnDropCompleted(object? sender, DropCompletedEventArgs e) => ChannelDrag.Reset();
}
