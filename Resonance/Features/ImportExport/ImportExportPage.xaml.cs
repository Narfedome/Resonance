using CommunityToolkit.Maui.Extensions;

namespace Resonance.Features.ImportExport;

public partial class ImportExportPage : ContentPage
{
    public ImportExportPage(ImportExportViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;

        // Cf. SettingsPage.xaml.cs : plafonne/centre la colonne sur Desktop uniquement, en code plutôt
        // qu'en XAML (MaximumWidthRequest n'a pas de valeur sentinelle "pas de contrainte" du genre
        // WidthRequest=-1 - le vrai défaut, PositiveInfinity, ne doit pas être touché sur Android/iOS).
        // WidthRequest (largeur exacte), pas juste MaximumWidthRequest : avec HorizontalOptions=Center,
        // un élément se centre à sa taille NATURELLE, pas à sa largeur maximale - Header (juste un
        // chevron + un titre, contenu étroit) se réduisait à son texte et se centrait comme un petit
        // bloc, décalé par rapport à ContentStack (dont le contenu veut naturellement remplir l'espace,
        // donc atteint déjà ~560 sous Center seul). WidthRequest force les deux à la même largeur
        // exacte, garantissant leur alignement.
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            ContentStack.WidthRequest = 560;
            ContentStack.HorizontalOptions = LayoutOptions.Center;
            Header.WidthRequest = 560;
            Header.HorizontalOptions = LayoutOptions.Center;
        }
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        if (args.WasPreviousPageACommunityToolkitPopupPage())
            return;

        if (BindingContext is ImportExportViewModel vm)
            await vm.InitializeAsync();
    }
}
