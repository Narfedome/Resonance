using Resonance.Services;

namespace Resonance.Components
{
    public partial class PalettePickerView : ContentView
    {
        public static readonly BindableProperty SelectedPaletteProperty =
            BindableProperty.Create(nameof(SelectedPalette), typeof(AppPalette), typeof(PalettePickerView),
                AppPalette.NuitEtOr, BindingMode.TwoWay, propertyChanged: (b, _, n) =>
                    ((PalettePickerView)b).UpdateSelection((AppPalette)n));

        public AppPalette SelectedPalette
        {
            get => (AppPalette)GetValue(SelectedPaletteProperty);
            set => SetValue(SelectedPaletteProperty, value);
        }

        public PalettePickerView()
        {
            InitializeComponent();
            UpdateSelection(SelectedPalette);
        }

        private void SelectA(object? sender, TappedEventArgs e) => SelectedPalette = AppPalette.NuitEtOr;
        private void SelectB(object? sender, TappedEventArgs e) => SelectedPalette = AppPalette.ForetProfonde;
        private void SelectC(object? sender, TappedEventArgs e) => SelectedPalette = AppPalette.TaverneAmbre;
        private void SelectD(object? sender, TappedEventArgs e) => SelectedPalette = AppPalette.AcierBrume;

        private void UpdateSelection(AppPalette palette)
        {
            SetBorder(BorderA, palette == AppPalette.NuitEtOr,      "#C9A84C");
            SetBorder(BorderB, palette == AppPalette.ForetProfonde,  "#7AAF5C");
            SetBorder(BorderC, palette == AppPalette.TaverneAmbre,   "#C47B3A");
            SetBorder(BorderD, palette == AppPalette.AcierBrume,     "#5B8FA8");
        }

        private static void SetBorder(Border border, bool selected, string accentHex)
        {
            border.StrokeThickness = selected ? 3 : 1;
            border.Stroke = selected
                ? new SolidColorBrush(Color.FromArgb(accentHex))
                : new SolidColorBrush(Colors.Transparent);
        }
    }
}
