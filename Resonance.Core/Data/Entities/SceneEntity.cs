using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Resonance.Data.Entities
{
    public class SceneEntity : IPositioned
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int SessionId { get; set; }

        public string Title { get; set; } = string.Empty;

        public int Position { get; set; }
    }
}
