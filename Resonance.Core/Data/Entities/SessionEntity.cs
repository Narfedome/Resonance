using SQLite;
using System;
using System.Collections.Generic;
using System.Text;

namespace Resonance.Data.Entities
{
    public class SessionEntity : IPositioned
    {
        [PrimaryKey, AutoIncrement]
        public int Id { get; set; }

        public int CampaignId { get; set; }

        public string Title { get; set; } = string.Empty;

        public int Position { get; set; }
    }
}
