using Resonance.Components;
using Resonance.Models.Library;

namespace Resonance.Features.Library;

public partial class LibrarySpellView : ContentView
{
    public LibrarySpellView()
	{
		InitializeComponent();
    }
        
    public static readonly BindableProperty IsCrudProperty =
    BindableProperty.Create(nameof(IsCrud), typeof(bool), typeof(LibrarySpellView), default(bool));

    public bool IsCrud
    {
        get => (bool)GetValue(IsCrudProperty);
        set => SetValue(IsCrudProperty, value);
    }
}