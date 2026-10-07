using CommunityToolkit.Maui.Extensions;

namespace Resonance.Features.Settings;

public partial class SettingsPage : ContentPage
{
    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        // Cf. commentaire XAML sur ContentStack : uniquement sur Desktop, pour ne pas risquer de
        // toucher au vrai défaut (PositiveInfinity) sur Android/iOS.
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            ContentStack.MaximumWidthRequest = 560;
            ContentStack.HorizontalOptions = LayoutOptions.Center;
        }
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        // Cf. LibraryTrackPage : les commandes Cleanup/DeleteAllTracks rafraîchissent déjà
        // StorageUsedText elles-mêmes après confirmation, pas besoin de le refaire ici.
        if (args.WasPreviousPageACommunityToolkitPopupPage())
            return;

        if (BindingContext is SettingsViewModel vm)
            await vm.InitializeAsync();
    }
}
