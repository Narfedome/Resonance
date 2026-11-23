using Resonance.Services;

namespace Resonance.Tests;

public class TextSearchTests
{
    [Theory]
    [InlineData("Évocation", "evocation")]
    [InlineData("  FORÊT  ", "foret")]
    [InlineData("Cœur", "coeur")]
    [InlineData("Ægir", "aegir")]
    [InlineData("Straße", "strasse")]
    [InlineData("Ça va ?", "ca va ?")]
    [InlineData("Taverne", "taverne")]
    public void Normalize_LowersAndRemovesAccentsAndLigatures(string text, string expected)
    {
        Assert.Equal(expected, TextSearch.Normalize(text));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalize_BlankText_IsEmpty(string? text)
    {
        Assert.Equal(string.Empty, TextSearch.Normalize(text));
    }
}
