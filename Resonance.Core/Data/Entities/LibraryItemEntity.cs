using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Resonance.Data.Entities
{
    public abstract class LibraryItemEntity
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public string Title { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }
}
