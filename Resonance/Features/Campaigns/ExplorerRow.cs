using CommunityToolkit.Mvvm.ComponentModel;
using Resonance.Models;

namespace Resonance.Features.Campaigns
{
    /// <summary>
    /// Ligne de l'accordéon Campagne/Chapitre/Scène : la liste affichée (CampaignViewModel.Rows) est
    /// une arborescence "à plat" — les lignes filles ne sont insérées/retirées de la collection qu'à
    /// l'expansion/repli de leur parent, plutôt que d'imbriquer des CollectionView (évite les soucis de
    /// virtualisation des CollectionView imbriquées sur Android).
    /// </summary>
    public abstract partial class ExplorerRow : ObservableObject
    {
        public abstract int Depth { get; }

        // Portée par la ligne elle-même (plutôt que par la sélection native du CollectionView, dont le
        // rendu diverge trop entre plateformes — gros bloc plein sur Android, simple liseré sur
        // Windows) : un seul style personnalisé, identique partout.
        [ObservableProperty]
        private bool isSelected;
    }

    public partial class CampaignRow : ExplorerRow, IExpandableRow
    {
        public override int Depth => 0;
        public Campaign Campaign { get; }

        [ObservableProperty]
        private bool isExpanded;

        // Tenu à jour par CampaignViewModel (RefreshFirstCampaignFlags) à chaque mutation de Rows :
        // la première campagne de la liste n'a pas besoin de l'espace ajouté au-dessus de chaque
        // campagne pour les séparer visuellement (cf. AppCampaignRowMargin) - sans quoi ça pousse
        // inutilement tout l'accordéon vers le bas.
        [ObservableProperty]
        private bool isFirstCampaign;

        public CampaignRow(Campaign campaign) => Campaign = campaign;
    }

    public partial class SessionRow : ExplorerRow, IExpandableRow
    {
        public override int Depth => 1;
        public Session Session { get; }
        public Campaign ParentCampaign { get; }

        [ObservableProperty]
        private bool isExpanded;

        public SessionRow(Session session, Campaign parentCampaign)
        {
            Session = session;
            ParentCampaign = parentCampaign;
        }
    }

    public class SceneRow : ExplorerRow
    {
        public override int Depth => 2;
        public Scene Scene { get; }
        public Session ParentSession { get; }
        public Campaign ParentCampaign { get; }

        public SceneRow(Scene scene, Session parentSession, Campaign parentCampaign)
        {
            Scene = scene;
            ParentSession = parentSession;
            ParentCampaign = parentCampaign;
        }
    }

    /// <summary>Lignes que le chevron peut déplier/replier (Campagne, Chapitre) — pas Scène.</summary>
    public interface IExpandableRow
    {
        bool IsExpanded { get; set; }
    }
}
