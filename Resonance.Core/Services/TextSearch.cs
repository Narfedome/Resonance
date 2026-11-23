using System.Globalization;
using System.Text;

namespace Resonance.Services
{
    /// <summary>
    /// Forme de recherche d'un texte : minuscules, sans accents ni ligatures. Stockée à côté du titre
    /// des pistes (TrackEntity.SearchTitle) parce que SQLite ne sait ni ignorer les accents ni mettre
    /// en minuscules autre chose que l'ASCII : "évocation" doit trouver "Évocation".
    /// </summary>
    public static class TextSearch
    {
        public static string Normalize(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var decomposed = text.Trim().ToLowerInvariant()
                .Replace("œ", "oe").Replace("æ", "ae").Replace("ß", "ss")
                .Normalize(NormalizationForm.FormD);

            var builder = new StringBuilder(decomposed.Length);
            foreach (var c in decomposed)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    builder.Append(c);
            }
            return builder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}
