namespace Resonance.Features.Campaigns
{
    public class ExplorerRowTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? CampaignTemplate { get; set; }
        public DataTemplate? SessionTemplate { get; set; }
        public DataTemplate? SceneTemplate { get; set; }

        protected override DataTemplate OnSelectTemplate(object item, BindableObject container) => item switch
        {
            CampaignRow => CampaignTemplate!,
            SessionRow => SessionTemplate!,
            SceneRow => SceneTemplate!,
            _ => throw new NotSupportedException($"Unknown row type: {item.GetType()}"),
        };
    }
}
