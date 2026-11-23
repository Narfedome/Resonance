using System;
using System.Collections.Generic;
using System.Text;

namespace Resonance.Data.Entities
{
    public class TrackEntity : LibraryItemEntity
    {

        public TimeSpan Duration { get; set; }

        public double DefaultVolume { get; set; }

        public string Hash { get; set; } = string.Empty;

        public string Category { get; set; } = string.Empty;

        /// <summary>Titre en forme de recherche (cf. TextSearch.Normalize), tenu à jour à chaque sauvegarde.</summary>
        public string SearchTitle { get; set; } = string.Empty;
    }
}
