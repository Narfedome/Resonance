namespace Resonance.Components;

public partial class MixerConsoleStripView : ContentView
{
    public MixerConsoleStripView()
    {
        InitializeComponent();
    }

    private void OnDragStarting(object? sender, DragStartingEventArgs e) => ChannelDrag.Start(this, e);

    private void OnDropCompleted(object? sender, DropCompletedEventArgs e) => ChannelDrag.Reset();
}
