namespace Resonance.Data.Entities
{
    /// <summary>Entités dont l'ordre parmi leurs frères est piloté par un champ Position (glisser-déposer).</summary>
    public interface IPositioned
    {
        int Position { get; set; }
    }
}
