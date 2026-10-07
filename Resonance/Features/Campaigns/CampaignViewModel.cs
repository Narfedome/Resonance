using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Components.Dialogs;
using Resonance.Extensions;
using Resonance.Features.AudioMixer;
using Resonance.Models;
using Resonance.Services;
using System.Collections.ObjectModel;

namespace Resonance.Features.Campaigns
{
    /// <summary>
    /// Accordéon Campagne → Chapitre → Scène sur une seule page : Rows est un arbre "à plat", les
    /// lignes filles ne sont insérées qu'à l'expansion de leur parent, et une seule branche par niveau
    /// reste dépliée à la fois (déplier une campagne/un chapitre replie son précédent sibling déplié).
    /// Un unique "+" (barre du bas) ouvre un formulaire générique (CreateItemDialog) où le type
    /// Campagne/Chapitre/Scène et son parent sont pré-remplis sur SelectedRow mais restent modifiables.
    /// </summary>
    public partial class CampaignViewModel : BaseViewModel
    {
        private readonly ISceneDataService _sceneDataService;
        private readonly AudioMixerViewModel _audioMixerViewModel;

        public CampaignViewModel(
            ISceneDataService sceneDataService,
            AudioMixerViewModel audioMixerViewModel)
        {
            _sceneDataService = sceneDataService;
            _audioMixerViewModel = audioMixerViewModel;

            // Les mutations normales (Create/Edit/Delete) patchent Rows directement, sans jamais
            // recharger depuis la base — mais un import ajoute des campagnes par un chemin qui ne
            // passe par aucune d'elles. CampaignPage ne recharge par ailleurs qu'une seule fois
            // (cf. son garde _initialized), donc sans ce message les campagnes importées restent
            // invisibles jusqu'au redémarrage de l'appli.
            WeakReferenceMessenger.Default.Register<CampaignsUpdatedMessage>(this,
            async (r, m) =>
            {
                await MainThread.InvokeOnMainThreadAsync(InitializeAsync);
            });
        }

        [ObservableProperty] private ObservableCollection<ExplorerRow> rows = new();
        [ObservableProperty] private ExplorerRow? selectedRow;
        [ObservableProperty] private bool hasCampaigns;

        // Reste false pendant le court instant entre l'affichage de la page et le vrai début du
        // chargement (Loading.IsLoading ne passe à true qu'à l'intérieur de InitializeAsync) : sans ce
        // garde-fou, HasCampaigns valait encore false par défaut durant cette fenêtre et l'état vide
        // (bouton "Créer") s'affichait brièvement avant le spinner.
        [ObservableProperty] private bool isInitialized;

        // IsSelected vit sur la ligne elle-même : le style de sélection (cf. CampaignPage.xaml) se lie
        // dessus plutôt que sur la sélection native du CollectionView (SelectionMode="None"), dont le
        // rendu par défaut diverge trop entre Windows et Android.
        partial void OnSelectedRowChanged(ExplorerRow? oldValue, ExplorerRow? newValue)
        {
            if (oldValue != null) oldValue.IsSelected = false;
            if (newValue != null) newValue.IsSelected = true;
        }

        /// <summary>Sélection d'une ligne sans action associée (Scène : Campagne/Chapitre sélectionnent déjà via ToggleCampaign/ToggleSession).</summary>
        [RelayCommand]
        public void Select(ExplorerRow row) => SelectedRow = row;

        public async Task InitializeAsync()
        {
            await Loading.RunAsync(async () =>
            {
                var campaigns = await _sceneDataService.GetCampaignsAsync();
                Rows = new ObservableCollection<ExplorerRow>(campaigns.Select(c => new CampaignRow(c)));
                RefreshFirstCampaignFlags();
                HasCampaigns = campaigns.Count > 0;
                SelectedRow = null;
                IsInitialized = true;
            });
        }

        // ── Expand / collapse ─────────────────────────────────────

        // Ouvrir une ligne et la sélectionner (pour Renommer/Supprimer) sont fondus en un seul geste.
        // Deux commandes distinctes selon la zone tapée (cf. CampaignPage.xaml) :
        // - Open* (corps de la ligne) n'ouvre jamais que — ne referme jamais un volet déjà ouvert,
        //   pour qu'on puisse retaper une campagne/un chapitre pour le sélectionner (Renommer/
        //   Supprimer) sans le refermer sous ses pieds pendant qu'on navigue dedans.
        // - Toggle* (le chevron uniquement) fait un vrai bascule, y compris la fermeture : son
        //   affordance visuelle appelle explicitement ce geste.
        // Dans les deux cas, un volet s'auto-referme dès qu'un AUTRE élément du même niveau s'ouvre
        // (cf. ExpandCampaignAsync/ExpandSessionAsync).
        [RelayCommand]
        public async Task OpenCampaign(CampaignRow row)
        {
            SelectedRow = row;
            if (!row.IsExpanded) await ExpandCampaignAsync(row);
        }

        [RelayCommand]
        public async Task OpenSession(SessionRow row)
        {
            SelectedRow = row;
            if (!row.IsExpanded) await ExpandSessionAsync(row);
        }

        [RelayCommand]
        public async Task ToggleCampaign(CampaignRow row)
        {
            SelectedRow = row;
            if (row.IsExpanded) CollapseCampaign(row);
            else await ExpandCampaignAsync(row);
        }

        [RelayCommand]
        public async Task ToggleSession(SessionRow row)
        {
            SelectedRow = row;
            if (row.IsExpanded) CollapseSession(row);
            else await ExpandSessionAsync(row);
        }

        private async Task ExpandCampaignAsync(CampaignRow row)
        {
            if (row.IsExpanded) return;

            var otherExpanded = Rows.OfType<CampaignRow>().FirstOrDefault(r => r.IsExpanded);
            if (otherExpanded != null) CollapseCampaign(otherExpanded);

            row.IsExpanded = true;
            var sessions = await _sceneDataService.GetSessionsAsync(row.Campaign.Id);
            int insertAt = Rows.IndexOf(row) + 1;
            foreach (var session in sessions)
                Rows.Insert(insertAt++, new SessionRow(session, row.Campaign));
        }

        private void CollapseCampaign(CampaignRow row)
        {
            if (!row.IsExpanded) return;

            row.IsExpanded = false;
            int idx = Rows.IndexOf(row) + 1;
            while (idx < Rows.Count && Rows[idx].Depth > 0)
                Rows.RemoveAt(idx);
        }

        private async Task ExpandSessionAsync(SessionRow row)
        {
            if (row.IsExpanded) return;

            var otherExpanded = Rows.OfType<SessionRow>().FirstOrDefault(r => r.IsExpanded);
            if (otherExpanded != null) CollapseSession(otherExpanded);

            row.IsExpanded = true;
            var scenes = await _sceneDataService.GetScenesAsync(row.Session.Id);
            int insertAt = Rows.IndexOf(row) + 1;
            foreach (var scene in scenes)
                Rows.Insert(insertAt++, new SceneRow(scene, row.Session, row.ParentCampaign));
        }

        private void CollapseSession(SessionRow row)
        {
            if (!row.IsExpanded) return;

            row.IsExpanded = false;
            int idx = Rows.IndexOf(row) + 1;
            while (idx < Rows.Count && Rows[idx].Depth > 1)
                Rows.RemoveAt(idx);
        }

        /// <summary>Index juste après le dernier descendant direct de <paramref name="row"/> (avant le prochain sibling ou la fin).</summary>
        private int EndOfSubtree(ExplorerRow row)
        {
            int idx = Rows.IndexOf(row) + 1;
            while (idx < Rows.Count && Rows[idx].Depth > row.Depth) idx++;
            return idx;
        }

        // ── Glisser-déposer (réordonnancement entre frères de même profondeur/parent) ─

        /// <summary>
        /// Replie la ligne si dépliée, avant de commencer à la glisser (cf. CampaignPage.xaml.cs,
        /// DragStarting) : on ne déplace jamais un sous-arbre entier par glisser-déposer, seulement
        /// la ligne elle-même — son sous-arbre resterait sinon éparpillé par le déplacement.
        /// </summary>
        public void CollapseRowForDrag(ExplorerRow row)
        {
            switch (row)
            {
                case CampaignRow c: CollapseCampaign(c); break;
                case SessionRow s: CollapseSession(s); break;
            }
        }

        /// <summary>
        /// Réordonne deux lignes glissées l'une sur l'autre. Ignoré silencieusement si elles ne sont
        /// pas de même profondeur ET même parent (glisser une Scène sur un Chapitre, ou un Chapitre
        /// d'une autre Campagne, n'a pas de sens).
        /// </summary>
        public async Task ReorderRowsAsync(ExplorerRow dragged, ExplorerRow target)
        {
            if (dragged == null || target == null || ReferenceEquals(dragged, target) || dragged.Depth != target.Depth)
                return;

            switch (dragged)
            {
                case CampaignRow draggedCampaign when target is CampaignRow:
                    await ReorderCampaignRowAsync(draggedCampaign, target);
                    break;
                case SessionRow draggedSession when target is SessionRow targetSession:
                    if (draggedSession.ParentCampaign.Id == targetSession.ParentCampaign.Id)
                        await ReorderSessionRowAsync(draggedSession, target);
                    break;
                case SceneRow draggedScene when target is SceneRow targetScene:
                    if (draggedScene.ParentSession.Id == targetScene.ParentSession.Id)
                        await ReorderSceneRowAsync(draggedScene, target);
                    break;
            }
        }

        // Rows contient TOUJOURS l'intégralité des frères d'un niveau visible au moment du drag
        // (Campagnes : jamais chargées à la demande ; Chapitres/Scènes : on ne peut voir/glisser une
        // ligne que si son parent est déplié, ce qui charge déjà tous ses enfants d'un coup) : filtrer
        // Rows après le déplacement suffit pour reconstituer l'ordre complet à persister, pas besoin
        // de retourner en base.

        private async Task ReorderCampaignRowAsync(CampaignRow dragged, ExplorerRow target)
        {
            int oldIndex = Rows.IndexOf(dragged);
            int newIndex = Rows.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0) return;

            Rows.Move(oldIndex, newIndex);
            RefreshFirstCampaignFlags();

            var orderedIds = Rows.OfType<CampaignRow>().Select(r => r.Campaign.Id).ToList();
            await _sceneDataService.ReorderCampaignsAsync(orderedIds);
        }

        private async Task ReorderSessionRowAsync(SessionRow dragged, ExplorerRow target)
        {
            int oldIndex = Rows.IndexOf(dragged);
            int newIndex = Rows.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0) return;

            Rows.Move(oldIndex, newIndex);

            var orderedIds = Rows.OfType<SessionRow>()
                .Where(r => r.ParentCampaign.Id == dragged.ParentCampaign.Id)
                .Select(r => r.Session.Id).ToList();
            await _sceneDataService.ReorderSessionsAsync(orderedIds);
        }

        private async Task ReorderSceneRowAsync(SceneRow dragged, ExplorerRow target)
        {
            int oldIndex = Rows.IndexOf(dragged);
            int newIndex = Rows.IndexOf(target);
            if (oldIndex < 0 || newIndex < 0) return;

            Rows.Move(oldIndex, newIndex);

            var orderedIds = Rows.OfType<SceneRow>()
                .Where(r => r.ParentSession.Id == dragged.ParentSession.Id)
                .Select(r => r.Scene.Id).ToList();
            await _sceneDataService.ReorderScenesAsync(orderedIds);
        }

        // ── Création (formulaire générique, type + parent pré-remplis sur SelectedRow) ─

        [RelayCommand]
        public async Task Create()
        {
            var campaigns = Rows.OfType<CampaignRow>().Select(r => r.Campaign).ToList();
            var (initialKind, initialCampaign, initialSession) = SelectedRow switch
            {
                CampaignRow c => (CreateItemKind.Session, c.Campaign, (Session?)null),
                SessionRow s => (CreateItemKind.Scene, s.ParentCampaign, (Session?)s.Session),
                SceneRow sc => (CreateItemKind.Scene, sc.ParentCampaign, (Session?)sc.ParentSession),
                _ => (CreateItemKind.Campaign, (Campaign?)null, (Session?)null),
            };

            var dialogViewModel = new CreateItemDialogViewModel(campaigns, _sceneDataService.GetSessionsAsync, initialKind, initialCampaign, initialSession);
            if (await ShowDialogAsync(new CreateItemDialog(dialogViewModel)) != true) return;

            string name = dialogViewModel.Name.Trim();
            if (string.IsNullOrWhiteSpace(name)) return;
            name = name.CapitalizeFirst();

            switch (dialogViewModel.SelectedKind)
            {
                case CreateItemKind.Campaign:
                {
                    // Position = nombre de campagnes existantes (nouvel élément en dernière position) :
                    // Rows contient TOUTES les campagnes (contrairement aux chapitres/scènes, chargés à la
                    // demande), donc campaigns.Count reflète déjà le nombre exact de frères.
                    var campaign = new Campaign { Title = name, Position = campaigns.Count };
                    await _sceneDataService.SaveCampaignAsync(campaign);
                    Rows.Add(new CampaignRow(campaign));
                    RefreshFirstCampaignFlags();
                    HasCampaigns = true;
                    break;
                }
                case CreateItemKind.Session:
                {
                    if (dialogViewModel.SelectedCampaign == null) return;
                    // Contrairement aux campagnes, Rows ne contient les chapitres que si leur campagne est
                    // dépliée : on relit le compte exact en base plutôt que de compter dans Rows.
                    var existingSessions = await _sceneDataService.GetSessionsAsync(dialogViewModel.SelectedCampaign.Id);
                    var session = new Session { CampaignId = dialogViewModel.SelectedCampaign.Id, Title = name, Position = existingSessions.Count };
                    await _sceneDataService.SaveSessionAsync(session);

                    var campaignRow = Rows.OfType<CampaignRow>().FirstOrDefault(r => r.Campaign.Id == dialogViewModel.SelectedCampaign.Id);
                    if (campaignRow != null && campaignRow.IsExpanded)
                        Rows.Insert(EndOfSubtree(campaignRow), new SessionRow(session, campaignRow.Campaign));
                    break;
                }
                case CreateItemKind.Scene:
                {
                    if (dialogViewModel.SelectedCampaign == null || dialogViewModel.SelectedSession == null) return;
                    var existingScenes = await _sceneDataService.GetScenesAsync(dialogViewModel.SelectedSession.Id);
                    var scene = new Scene { SessionId = dialogViewModel.SelectedSession.Id, Title = name, Position = existingScenes.Count };
                    await _sceneDataService.SaveSceneAsync(scene);

                    var sessionRow = Rows.OfType<SessionRow>().FirstOrDefault(r => r.Session.Id == dialogViewModel.SelectedSession.Id);
                    if (sessionRow != null && sessionRow.IsExpanded)
                        Rows.Insert(EndOfSubtree(sessionRow), new SceneRow(scene, sessionRow.Session, sessionRow.ParentCampaign));
                    break;
                }
            }
        }

        // ── Édition (réutilise le formulaire de création, type verrouillé) ───

        // Réutilise CreateItemDialog plutôt qu'un simple prompt texte : puisque le Campagne/Chapitre
        // parent y est déjà modifiable pour la création, éditer un Chapitre ou une Scène permet du
        // même coup de le/la déplacer vers une autre Campagne/un autre Chapitre (le Type, lui, reste
        // verrouillé — changer la nature d'un élément existant n'a pas de sens).
        [RelayCommand]
        public async Task Edit()
        {
            var campaigns = Rows.OfType<CampaignRow>().Select(r => r.Campaign).ToList();

            switch (SelectedRow)
            {
                case CampaignRow campaignRow:
                {
                    var dialogViewModel = new CreateItemDialogViewModel(campaigns, _sceneDataService.GetSessionsAsync,
                        CreateItemKind.Campaign, null, null, campaignRow.Campaign.Title, lockType: true);
                    if (await ShowDialogAsync(new CreateItemDialog(dialogViewModel)) != true) return;
                    string name = dialogViewModel.Name.Trim();
                    if (string.IsNullOrWhiteSpace(name)) return;

                    campaignRow.Campaign.Title = name.CapitalizeFirst();
                    await _sceneDataService.SaveCampaignAsync(campaignRow.Campaign);
                    break;
                }
                case SessionRow sessionRow:
                {
                    var dialogViewModel = new CreateItemDialogViewModel(campaigns, _sceneDataService.GetSessionsAsync,
                        CreateItemKind.Session, sessionRow.ParentCampaign, null, sessionRow.Session.Title, lockType: true);
                    if (await ShowDialogAsync(new CreateItemDialog(dialogViewModel)) != true) return;
                    string name = dialogViewModel.Name.Trim();
                    if (string.IsNullOrWhiteSpace(name) || dialogViewModel.SelectedCampaign == null) return;

                    sessionRow.Session.Title = name.CapitalizeFirst();
                    bool moved = dialogViewModel.SelectedCampaign.Id != sessionRow.Session.CampaignId;
                    sessionRow.Session.CampaignId = dialogViewModel.SelectedCampaign.Id;
                    if (moved)
                    {
                        // Repart en dernière position parmi les chapitres de la nouvelle campagne,
                        // plutôt que de garder sa Position d'origine (qui n'a plus de sens dans un
                        // autre groupe de frères, et pourrait entrer en collision avec l'un d'eux).
                        var existingSessions = await _sceneDataService.GetSessionsAsync(dialogViewModel.SelectedCampaign.Id);
                        sessionRow.Session.Position = existingSessions.Count;
                    }
                    await _sceneDataService.SaveSessionAsync(sessionRow.Session);

                    if (moved)
                    {
                        RemoveSubtree(sessionRow);
                        var targetCampaignRow = Rows.OfType<CampaignRow>().FirstOrDefault(r => r.Campaign.Id == dialogViewModel.SelectedCampaign.Id);
                        if (targetCampaignRow != null && targetCampaignRow.IsExpanded)
                            Rows.Insert(EndOfSubtree(targetCampaignRow), new SessionRow(sessionRow.Session, dialogViewModel.SelectedCampaign));
                        SelectedRow = null;
                    }
                    break;
                }
                case SceneRow sceneRow:
                {
                    var dialogViewModel = new CreateItemDialogViewModel(campaigns, _sceneDataService.GetSessionsAsync,
                        CreateItemKind.Scene, sceneRow.ParentCampaign, sceneRow.ParentSession, sceneRow.Scene.Title, lockType: true);
                    if (await ShowDialogAsync(new CreateItemDialog(dialogViewModel)) != true) return;
                    string name = dialogViewModel.Name.Trim();
                    if (string.IsNullOrWhiteSpace(name) || dialogViewModel.SelectedCampaign == null || dialogViewModel.SelectedSession == null) return;

                    sceneRow.Scene.Title = name.CapitalizeFirst();
                    bool moved = dialogViewModel.SelectedSession.Id != sceneRow.Scene.SessionId;
                    sceneRow.Scene.SessionId = dialogViewModel.SelectedSession.Id;
                    if (moved)
                    {
                        var existingScenes = await _sceneDataService.GetScenesAsync(dialogViewModel.SelectedSession.Id);
                        sceneRow.Scene.Position = existingScenes.Count;
                    }
                    await _sceneDataService.SaveSceneAsync(sceneRow.Scene);

                    if (moved)
                    {
                        Rows.Remove(sceneRow);
                        var targetSessionRow = Rows.OfType<SessionRow>().FirstOrDefault(r => r.Session.Id == dialogViewModel.SelectedSession.Id);
                        if (targetSessionRow != null && targetSessionRow.IsExpanded)
                            Rows.Insert(EndOfSubtree(targetSessionRow), new SceneRow(sceneRow.Scene, dialogViewModel.SelectedSession, dialogViewModel.SelectedCampaign));
                        SelectedRow = null;
                    }
                    break;
                }
            }
        }

        [RelayCommand]
        public async Task Delete()
        {
            switch (SelectedRow)
            {
                case CampaignRow campaignRow:
                    if (!await ConfirmDeleteAsync(campaignRow.Campaign.Title)) return;
                    // DeleteCampaignAsync cascade déjà les Session/Scene/SceneTrack en base ; ici il
                    // faut aussi retirer ses éventuelles lignes Chapitre/Scène dépliées de Rows, sinon
                    // elles restent affichées, orphelines, sans plus aucune campagne au-dessus d'elles.
                    await _sceneDataService.DeleteCampaignAsync(campaignRow.Campaign);
                    RemoveSubtree(campaignRow);
                    HasCampaigns = Rows.OfType<CampaignRow>().Any();
                    if (_audioMixerViewModel.IsActiveCampaign(campaignRow.Campaign.Id))
                        DeactivateMixer();
                    break;
                case SessionRow sessionRow:
                    if (!await ConfirmDeleteAsync(sessionRow.Session.Title)) return;
                    await _sceneDataService.DeleteSessionAsync(sessionRow.Session);
                    RemoveSubtree(sessionRow);
                    if (_audioMixerViewModel.IsActiveSession(sessionRow.Session.Id))
                        DeactivateMixer();
                    break;
                case SceneRow sceneRow:
                    if (!await ConfirmDeleteAsync(sceneRow.Scene.Title)) return;
                    await _sceneDataService.DeleteSceneAsync(sceneRow.Scene);
                    Rows.Remove(sceneRow);
                    if (_audioMixerViewModel.IsActiveScene(sceneRow.Scene.Id))
                        DeactivateMixer();
                    break;
                default:
                    return;
            }

            SelectedRow = null;
        }

        /// <summary>
        /// La scène/chapitre/campagne supprimée était affichée dans le mixer : sans ça, il
        /// continuerait de pointer sur des données qui n'existent plus en base.
        /// </summary>
        private void DeactivateMixer()
        {
            _audioMixerViewModel.ResetActiveScene();
        }

        private void RemoveSubtree(ExplorerRow row)
        {
            int idx = Rows.IndexOf(row);
            int end = EndOfSubtree(row);
            for (int i = end - 1; i >= idx; i--)
                Rows.RemoveAt(i);
            RefreshFirstCampaignFlags();
        }

        /// <summary>
        /// Marque la toute première CampaignRow de Rows (IsFirstCampaign) : elle n'a pas besoin de
        /// l'espace ajouté au-dessus de chaque campagne pour les séparer visuellement (cf.
        /// AppCampaignRowMargin dans CampaignPage.xaml), sans quoi ça pousse tout l'accordéon vers le
        /// bas inutilement. À rappeler après toute mutation de Rows qui pourrait changer laquelle est
        /// la première (chargement initial, ajout/suppression d'une campagne).
        /// </summary>
        private void RefreshFirstCampaignFlags()
        {
            bool isFirst = true;
            foreach (var campaignRow in Rows.OfType<CampaignRow>())
            {
                campaignRow.IsFirstCampaign = isFirst;
                isFirst = false;
            }
        }

        // Navigue d'abord, charge ensuite : LoadFromPlayAsync tourne sous l'overlay de
        // AudioMixerPage (visible une fois la navigation faite), au lieu de travailler en
        // silence sur la page Campagnes pendant que l'utilisateur ne voit toujours rien bouger.
        [RelayCommand]
        public async Task Launch(SceneRow sceneRow)
        {
            // Avant de naviguer : empêche AudioMixerPage.OnAppearing de charger la scène orpheline
            // entre-temps (cf. AudioMixerViewModel.SuppressNextFreeformAutoLoad).
            _audioMixerViewModel.SuppressNextFreeformAutoLoad = true;
            await Shell.Current.GoToAsync("//AudioMixerPage");
            await _audioMixerViewModel.LoadFromPlayAsync(sceneRow.ParentCampaign, sceneRow.ParentSession, sceneRow.Scene);
        }
    }
}
