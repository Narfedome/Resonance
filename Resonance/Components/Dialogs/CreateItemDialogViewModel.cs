using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Models;
using Resonance.Resources.Icons;
using Resonance.Services;

namespace Resonance.Components.Dialogs
{
    public enum CreateItemKind { Campaign, Session, Scene }

    /// <summary>
    /// Logique du formulaire générique de création/édition (Campagne/Chapitre/Scène) -
    /// CreateItemDialog n'est qu'un wrapper XAML lié dessus. Le type et le parent sont pré-remplis
    /// sur la sélection courante de l'accordéon (cf. CampaignViewModel.Create/Edit) mais restent
    /// modifiables — éditer un Chapitre/une Scène permet donc aussi de le/la déplacer vers une autre
    /// Campagne/un autre Chapitre. Ne persiste rien lui-même : l'appelant lit SelectedKind/Name/
    /// SelectedCampaign/SelectedSession une fois le dialogue confirmé.
    /// </summary>
    public partial class CreateItemDialogViewModel : DialogViewModel<bool>
    {
        private readonly List<Campaign> _campaigns;
        private readonly Func<int, Task<List<Session>>> _loadSessions;
        private readonly Campaign? _initialCampaign;
        private readonly Session? _initialSession;
        private List<Session> _sessions = new();

        protected override bool CancelResult => false;

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private CreateItemKind kind;

        [ObservableProperty]
        private string typeButtonText = string.Empty;

        [ObservableProperty]
        private string typeButtonIcon = string.Empty;

        [ObservableProperty]
        private bool typeButtonEnabled = true;

        [ObservableProperty]
        private bool campaignFieldVisible;

        [ObservableProperty]
        private string campaignButtonText = string.Empty;

        [ObservableProperty]
        private bool sessionFieldVisible;

        [ObservableProperty]
        private string sessionButtonText = string.Empty;

        public CreateItemKind SelectedKind => Kind;
        public Campaign? SelectedCampaign { get; private set; }
        public Session? SelectedSession { get; private set; }

        /// <param name="lockType">Édition d'un élément existant : changer sa nature (Campagne/Chapitre/
        /// Scène) n'a pas de sens, seul le nom et le parent (déplacement) restent modifiables.</param>
        public CreateItemDialogViewModel(
            List<Campaign> campaigns,
            Func<int, Task<List<Session>>> loadSessions,
            CreateItemKind initialKind,
            Campaign? initialCampaign,
            Session? initialSession,
            string? initialName = null,
            bool lockType = false)
        {
            _campaigns = campaigns;
            _loadSessions = loadSessions;
            _initialCampaign = initialCampaign;
            _initialSession = initialSession;
            // Pas de campagne existante : seul "Campagne" a un sens, les autres n'auraient aucun parent.
            Kind = campaigns.Count == 0 ? CreateItemKind.Campaign : initialKind;

            Title = Loc[lockType ? "DialogEditItem" : "DialogCreateItem"];
            Name = initialName ?? string.Empty;
            TypeButtonEnabled = !lockType;
            UpdateTypeButtonText();
            UpdateFieldVisibility();
        }

        /// <summary>Présélectionne la campagne/le chapitre initiaux (charge les chapitres associés) :
        /// appelé par CreateItemDialog à l'ouverture, une fois le dialogue effectivement affiché.</summary>
        public async Task InitializeAsync()
        {
            var campaign = _initialCampaign ?? _campaigns.FirstOrDefault();
            if (campaign != null)
                await SelectCampaignAsync(campaign, _initialSession);

            UpdateTypeButtonText();
            UpdateFieldVisibility();
        }

        [RelayCommand]
        public async Task SelectType()
        {
            var kinds = AvailableKinds();
            int index = await ShowActionSheetIndexAsync(Loc["FieldType"], kinds.Select(NounFor).ToArray());
            if (index < 0) return;

            Kind = kinds[index];
            UpdateTypeButtonText();
            UpdateFieldVisibility();
        }

        [RelayCommand]
        public async Task SelectCampaign()
        {
            if (_campaigns.Count == 0) return;
            int index = await ShowActionSheetIndexAsync(Loc["NounCampaign"], _campaigns.Select(c => c.Title).ToArray());
            if (index < 0) return;

            await SelectCampaignAsync(_campaigns[index], null);
            UpdateTypeButtonText();
            UpdateFieldVisibility();
        }

        [RelayCommand]
        public async Task SelectSession()
        {
            if (_sessions.Count == 0) return;
            int index = await ShowActionSheetIndexAsync(Loc["NounChapter"], _sessions.Select(s => s.Title).ToArray());
            if (index < 0) return;

            SelectedSession = _sessions[index];
            SessionButtonText = SelectedSession.Title;
        }

        private async Task SelectCampaignAsync(Campaign campaign, Session? preselect)
        {
            SelectedCampaign = campaign;
            CampaignButtonText = campaign.Title;

            _sessions = await _loadSessions(campaign.Id);
            SelectedSession = preselect != null
                ? _sessions.FirstOrDefault(s => s.Id == preselect.Id) ?? _sessions.FirstOrDefault()
                : _sessions.FirstOrDefault();
            SessionButtonText = SelectedSession?.Title ?? string.Empty;

            // Cette campagne n'a aucun chapitre : "Scène" n'a plus de parent possible.
            if (_sessions.Count == 0 && Kind == CreateItemKind.Scene)
                Kind = CreateItemKind.Session;
        }

        /// <summary>Types proposables dans l'état courant : Chapitre exige au moins une campagne, Scène exige que la campagne sélectionnée ait au moins un chapitre.</summary>
        private List<CreateItemKind> AvailableKinds()
        {
            var kinds = new List<CreateItemKind> { CreateItemKind.Campaign };
            if (_campaigns.Count > 0) kinds.Add(CreateItemKind.Session);
            if (_campaigns.Count > 0 && _sessions.Count > 0) kinds.Add(CreateItemKind.Scene);
            return kinds;
        }

        private static string NounFor(CreateItemKind kind) => kind switch
        {
            CreateItemKind.Campaign => LocalizationService.Instance["NounCampaign"],
            CreateItemKind.Session => LocalizationService.Instance["NounChapter"],
            CreateItemKind.Scene => LocalizationService.Instance["NounScene"],
            _ => string.Empty,
        };

        private static string IconFor(CreateItemKind kind) => kind switch
        {
            CreateItemKind.Campaign => SolidFont.Map,
            CreateItemKind.Session => SolidFont.Bookmark,
            CreateItemKind.Scene => SolidFont.MasksTheater,
            _ => string.Empty,
        };

        private void UpdateTypeButtonText()
        {
            TypeButtonText = NounFor(Kind);
            TypeButtonIcon = IconFor(Kind);
        }

        private void UpdateFieldVisibility()
        {
            CampaignFieldVisible = Kind != CreateItemKind.Campaign;
            SessionFieldVisible = Kind == CreateItemKind.Scene;
        }

        [RelayCommand]
        public void Save() => Close(true);
    }
}
