using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Resonance.Data.Entities
{
    public class CharacterEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }
    }
}
