using Microsoft.Maui.Controls;

namespace Resonance.Components
{
    public partial class LibraryItemImageView : ContentView
    {
        public LibraryItemImageView()
        {
            InitializeComponent();
        }

        public static readonly BindableProperty ImagePathProperty =
            BindableProperty.Create(nameof(ImagePath), typeof(string), typeof(LibraryItemImageView), "");

        public string ImagePath
        {
            get => (string)GetValue(ImagePathProperty);
            set => SetValue(ImagePathProperty, value);
        }

        public static readonly BindableProperty DefaultGlyphProperty =
            BindableProperty.Create(nameof(DefaultGlyph), typeof(string), typeof(LibraryItemImageView)); 

        public string DefaultGlyph
        {
            get => (string)GetValue(DefaultGlyphProperty);
            set => SetValue(DefaultGlyphProperty, value);
        }

        public static readonly BindableProperty FontFamilyProperty =
            BindableProperty.Create(nameof(FontFamily), typeof(string), typeof(LibraryItemImageView), "FontSolid");

        public string FontFamily
        {
            get => (string)GetValue(FontFamilyProperty);
            set => SetValue(FontFamilyProperty, value);
        }
    }
}