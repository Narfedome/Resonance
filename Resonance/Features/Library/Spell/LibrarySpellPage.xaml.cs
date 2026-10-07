using CommunityToolkit.Maui.Extensions;

namespace Resonance.Features.Library;

public partial class LibrarySpellPage : ContentPage
{
    public LibrarySpellPage(LibrarySpellViewModel vm)
    {
        InitializeComponent();
        BindingContext = vm;
    }

    protected override async void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);

        // Fermer une popup thémée (confirmation, saisie...) redéclenche aussi la navigation sur cette
        // page : dans ce cas le ViewModel a déjà mis à jour SpellItems localement, un rechargement complet
        // ferait doublon et pourrait entrer en collision avec cette mise à jour (liste qui clignote/se perd).
        if (args.WasPreviousPageACommunityToolkitPopupPage())
            return;

        if (BindingContext is LibrarySpellViewModel vm)
            await vm.InitializeAsync();
    }
}