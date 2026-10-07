using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Resonance.Data.Entities
{
    public class CharacterSpellEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int CharacterId { get; set; }

        public int SpellId { get; set; }

        public int Position { get; set; }
    }
}
