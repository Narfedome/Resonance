using System;
using System.Globalization;

namespace Resonance.Converters
{
    public class InverseBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;

            return false;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool boolValue)
                return !boolValue;

            return false;
        }
    }

    public class IsNotNullConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value != null;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }

    // Cache un contrôle sans lui retirer sa place dans le layout (contrairement à IsVisible, qui
    // collapse l'espace en MAUI - pas d'équivalent direct du Visibility.Hidden de WPF) : utilisé
    // avec InverseBoolConverter sur InputTransparent pour aussi bloquer le tap, sur les flèches de
    // navigation du Mixer par exemple (les masquer plutôt que les griser en bout de scène ne doit
    // pas faire sauter les pickers voisins).
    public class BoolToOpacityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is bool b && b ? 1d : 0d;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotImplementedException();
    }
}
