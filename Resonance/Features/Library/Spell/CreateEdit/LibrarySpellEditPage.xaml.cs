namespace Resonance.Features.Library;

public partial class LibrarySpellEditPage : ContentPage
{
	public LibrarySpellEditPage(LibrarySpellEditViewModel viewModel)
	{
		InitializeComponent();
		BindingContext = viewModel;
	}
}