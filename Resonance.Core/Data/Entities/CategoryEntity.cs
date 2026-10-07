using SQLite;

namespace Resonance.Data.Entities
{
    public class CategoryEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Name { get; set; } = string.Empty;

        // Nom du type de bibliothèque propriétaire (ex: "Track", "Spell") - les catégories sont
        // scopées par type pour éviter que les catégories audio (Musique/Ambiance...) apparaissent
        // dans les sorts et inversement.
        public string LibraryType { get; set; } = string.Empty;
    }
}
