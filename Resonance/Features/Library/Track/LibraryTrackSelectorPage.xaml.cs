namespace Resonance.Features.Library;

public partial class LibraryTrackSelectorPage : ContentPage
{
    private readonly LibraryTrackViewModel viewModel;

    public LibraryTrackSelectorPage(LibraryTrackViewModel vm)
    {
        InitializeComponent();
        viewModel = vm;
        BindingContext = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await viewModel.InitializeAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        if (BindingContext is LibraryTrackViewModel vm)
        {
            // Stop any playing audio when leaving the library page via the ViewModel (use DI)
            vm.StopAudio();

            // Interrompt un chargement en cours (cf. LibraryTrackViewModel._loadCts) plutôt que de le
            // laisser continuer en arrière-plan pendant qu'aucune tuile n'est visible pour en profiter.
            vm.CancelPendingLoad();
        }
    }
}