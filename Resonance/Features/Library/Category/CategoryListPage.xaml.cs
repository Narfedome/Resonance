using CommunityToolkit.Maui.Extensions;

namespace Resonance.Features.Library;

public partial class CategoryListPage : ContentPage
{
    private readonly CategoryListViewModel _vm;

    public CategoryListPage(CategoryListViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        BindingContext = vm;

        // Cf. SettingsPage.xaml.cs : plafonne/centre la liste sur Desktop uniquement, en code plutôt
        // qu'en XAML (MaximumWidthRequest n'a pas de valeur sentinelle "pas de contrainte" du genre
        // WidthRequest=-1 - le vrai défaut, PositiveInfinity, ne doit pas être touché sur Android/iOS).
        // WidthRequest (largeur exacte), pas juste MaximumWidthRequest : avec HorizontalOptions=Center,
        // un élément se centre à sa taille NATURELLE, pas à sa largeur maximale - Header (juste un
        // chevron + un titre, contenu étroit) se réduisait à son texte et se centrait comme un petit
        // bloc, décalé par rapport à CategoryCollection (dont le contenu veut naturellement remplir
        // l'espace, donc atteint déjà ~560 sous Center seul). WidthRequest force les deux à la même
        // largeur exacte, garantissant leur alignement.
        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            CategoryCollection.WidthRequest = 560;
            CategoryCollection.HorizontalOptions = LayoutOptions.Center;
            Header.WidthRequest = 560;
            Header.HorizontalOptions = LayoutOptions.Center;
        }
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        // Cf. LibraryTrackPage/CampaignPage : une popup thémée qui se ferme redéclenche la navigation,
        // mais le ViewModel a déjà mis à jour CategoryNames localement (Rename/Delete).
        if (args.WasPreviousPageACommunityToolkitPopupPage())
            return;

        await _vm.InitializeAsync();
    }
}
