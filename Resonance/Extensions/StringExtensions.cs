using System.Globalization;

namespace Resonance.Extensions
{
    public static class StringExtensions
    {
        public static string CapitalizeFirst(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return char.ToUpper(value[0], CultureInfo.CurrentCulture) + value[1..];
        }
    }
}
