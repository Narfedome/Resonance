using Resonance.Components.TrackButton;
using Resonance.Models.Library;
using Resonance.Services;
using System.Windows.Input;

namespace Resonance.Components;

public partial class TrackButtonView : ContentView
{
    // Expose ViewModel as a BindableProperty so XAML bindings that reference root.ViewModel.* update when it's created.
    public static readonly BindableProperty ViewModelProperty =
        BindableProperty.Create(nameof(ViewModel), typeof(TrackButtonViewModel), typeof(TrackButtonView), default(TrackButtonViewModel));

    public TrackButtonViewModel? ViewModel
    {
        get => (TrackButtonViewModel?)GetValue(ViewModelProperty);
        private set => SetValue(ViewModelProperty, value);
    }

    // Command to notify parent that this item was selected (bound from parent ViewModel)
    public static readonly BindableProperty SelectCommandProperty =
        BindableProperty.Create(nameof(SelectCommand), typeof(ICommand), typeof(TrackButtonView), default(ICommand));

    public ICommand? SelectCommand
    {
        get => (ICommand?)GetValue(SelectCommandProperty);
        set => SetValue(SelectCommandProperty, value);
    }

    public TrackButtonView()
    {
        InitializeComponent();
        // Do not create the ViewModel here — MauiContext/Services may not be available at XAML inflation time.
        // The ViewModel will be created lazily when needed (handler set or CurrentTrack assigned).
    }

    protected override void OnHandlerChanged()
    {
        base.OnHandlerChanged();

        // Handler null = vue détachée de l'arbre visuel : on désabonne le ViewModel du service
        // audio singleton, sinon chaque tuile créée (pagination, rechargements) reste accrochée
        // au service à vie. Ré-attachée (recyclage), la vue se réabonne et resynchronise.
        if (Handler == null)
        {
            ViewModel?.Detach();
            return;
        }

        EnsureViewModel();
        ViewModel?.Attach();
    }

    private void EnsureViewModel()
    {
        if (ViewModel != null)
            return;

        var services = Application.Current?
            .Handler?
            .MauiContext?
            .Services;

        var audioService = services?.GetService<AudioPlayerService>();
        var coverArtService = services?.GetService<CoverArtService>();

        if (audioService == null || coverArtService == null)
            return;

        ViewModel = new TrackButtonViewModel(audioService, coverArtService);

        // If the XAML child control exists, bind it to the ViewModel so its inner bindings use the VM as source.
        if (IconBtn != null)
        {
            IconBtn.BindingContext = ViewModel;

            // Create a composite command: toggle play then notify parent selection command (if any)
            IconBtn.Command = new Command(() =>
            {
                ViewModel?.TogglePlayCommand.Execute(null);
                SelectCommand?.Execute(CurrentTrack);
            });
        }

        // Forward any already-set CurrentTrack to the ViewModel
        if (CurrentTrack != null)
        {
            ViewModel.CurrentTrack = CurrentTrack;
        }
    }

    public static readonly BindableProperty CurrentTrackProperty =  BindableProperty.Create(nameof(CurrentTrack), typeof(Track), typeof(TrackButtonView), default(Track),
        propertyChanged: OnCurrentTrackChanged);

    public Track? CurrentTrack
    {
        get => (Track?)GetValue(CurrentTrackProperty);
        set => SetValue(CurrentTrackProperty, value);
    }

    private static void OnCurrentTrackChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (TrackButtonView)bindable;

        // Ensure the ViewModel exists before forwarding the value
        view.EnsureViewModel();

        if (view.ViewModel != null)
        {
            view.ViewModel.CurrentTrack = newValue as Track;
        }
    }
}