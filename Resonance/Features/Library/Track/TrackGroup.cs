using System.Collections.ObjectModel;
using Resonance.Models.Library;

namespace Resonance.Features.Library
{
    /// <summary>Section de la grille de pistes regroupées par catégorie ("Tout" affiche une CollectionView
    /// groupée) - Name est le libellé affiché en en-tête (nom de catégorie, ou "Aucun dossier").</summary>
    public class TrackGroup : ObservableCollection<Track>
    {
        public string Name { get; }

        public TrackGroup(string name) => Name = name;
    }
}
