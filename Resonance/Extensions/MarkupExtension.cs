namespace Resonance.Extensions
{
    public class FaIconExtension : IMarkupExtension<ImageSource>
    {
        public string Glyph { get; set; } = null!;

        public Color Color { get; set; } = Colors.Black;

        public double Size { get; set; } = 20;

        public ImageSource ProvideValue(IServiceProvider serviceProvider)
        {
            return new FontImageSource
            {
                Glyph = Glyph,
                FontFamily = "FontSolid", // doit matcher ton AddFont
                Color = Color,
                Size = Size
            };
        }

        object IMarkupExtension.ProvideValue(IServiceProvider serviceProvider)
            => ProvideValue(serviceProvider);
    }
}
