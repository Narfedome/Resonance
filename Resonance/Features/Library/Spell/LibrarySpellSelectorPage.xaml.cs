using Resonance.Models.Library;
using Resonance.Services;

namespace Resonance.Features.Library;

public partial class LibrarySpellSelectorPage : ContentPage
{
    private readonly LibrarySpellViewModel viewModel;

    public LibrarySpellSelectorPage(LibrarySpellViewModel vm)
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
}