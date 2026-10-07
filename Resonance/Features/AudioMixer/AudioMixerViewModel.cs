using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Models;
using Resonance.Services;
using System.Collections.ObjectModel;

namespace Resonance.Features.AudioMixer
{
    // Sélection de campagne/chapitre/scène, navigation, mode "sans scène" et cycle de vie de la
    // scène active. La gestion des channel strips (CRUD, players, sauvegarde debouncée, reorder)
    // est dans la partie AudioMixerViewModel.Channels.cs - les deux se partagent CurrentChannels et
    // _activeScene, mais suivent des logiques indépendantes assez volumineuses pour justifier des
    // fichiers séparés plutôt qu'une seule classe de 800+ lignes.
    public partial class AudioMixerViewModel : BaseViewModel
    {
        private readonly AudioMixerService _audioMixerService;
        private readonly ILibraryPickerService _pickerService;
        private readonly ISceneDataService _sceneDataService;

        private Scene? _activeScene;

        public AudioMixerViewModel(
            AudioMixerService audioMixerService,
            ILibraryPickerService pickerService,
            ISceneDataService sceneDataService)
        {
            _audioMixerService = audioMixerService;
            _pickerService = pickerService;
            _sceneDataService = sceneDataService;

            // SelectedSessionLabel/SelectedSceneLabel lisent Loc[...] dans leur getter mais ne sont
            // notifiées que par les changements de SelectedSession/SelectedScene/IsFreeformActive :
            // sans cet abonnement, changer de langue pendant qu'on est en freeform (où le libellé
            // affiché vient justement de Loc et pas d'un titre de scène) laissait l'ancien texte
            // affiché jusqu'au prochain changement de scène.
            WeakReferenceMessenger.Default.Register<LanguageChangedMessage>(this, (r, m) =>
            {
                var vm = (AudioMixerViewModel)r;
                vm.OnPropertyChanged(nameof(SelectedSessionLabel));
                vm.OnPropertyChanged(nameof(SelectedSceneLabel));
            });

            // Les repères visuels du channel strip (fond "en lecture", bandes de fade) lisent leur
            // couleur directement depuis ThemeService dans leurs converters, en contournant le
            // mécanisme DynamicResource qui se rafraîchit tout seul (cf. AppAccent/AppBorder) : sans
            // cet abonnement, changer de thème/palette pendant qu'une scène est chargée laissait ces
            // repères affichés dans l'ancienne couleur jusqu'au prochain changement d'état (play,
            // fade...). Même filet déjà posé pour le thumb du slider natif sous Windows, cf.
            // WindowsHandlers.cs.
            ThemeService.Instance.ThemeChanged += () =>
            {
                foreach (var channel in CurrentChannels)
                    channel.RefreshThemeDependentBindings();
            };
        }

        // ── Sélecteur de scène ────────────────────────────────────

        [ObservableProperty]
        private ObservableCollection<Session> sessions = new();

        [ObservableProperty]
        private ObservableCollection<Scene> scenes = new();

        [ObservableProperty]
        private Session? selectedSession;

        [ObservableProperty]
        private Scene? selectedScene;

        [ObservableProperty]
        private int sceneIndex = 1;

        [ObservableProperty]
        private int sceneCount = 0;

        // Actif quand le Mixer tourne sur la scène orpheline (cf. SelectFreeformScene), en dehors
        // de toute campagne/chapitre : SelectedSession/SelectedScene restent alors null (rien à
        // afficher dans le sélecteur), d'où ce flag séparé pour piloter les libellés.
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(SelectedSessionLabel))]
        [NotifyPropertyChangedFor(nameof(SelectedSceneLabel))]
        private bool isFreeformActive;

        // Permettent aux flèches prev/next de franchir une limite de chapitre (cf.
        // RefreshChapterBoundaryFlagsAsync) : au bout de la scène courante, on regarde si le
        // chapitre voisin existe ET a au moins une scène - jamais de saut en cascade jusqu'au
        // prochain chapitre non-vide, comportement prévisible plutôt que surprenant.
        private bool _hasPreviousChapterScene;
        private bool _hasNextChapterScene;

        public bool CanGoPrevScene => SceneIndex > 1 || _hasPreviousChapterScene;
        public bool CanGoNextScene => SceneIndex < SceneCount || _hasNextChapterScene;

        public string SelectedSessionLabel => IsFreeformActive ? Loc["MixerFreeform"] : SelectedSession?.Title ?? Loc["MixerChapter"];
        public string SelectedSceneLabel   => IsFreeformActive ? Loc["MixerFreeform"] : SelectedScene?.Title   ?? Loc["MixerScene"];

        private bool _suppressHandlers;

        partial void OnSelectedSessionChanged(Session? value)
        {
            OnPropertyChanged(nameof(SelectedSessionLabel));
            if (_suppressHandlers) return;
            SelectedScene = null;
            Scenes.Clear();
            if (value != null)
                _ = LoadScenesAfterSessionChangeAsync(value.Id);
        }

        partial void OnSelectedSceneChanged(Scene? value)
        {
            OnPropertyChanged(nameof(SelectedSceneLabel));
            if (_suppressHandlers) return;
            SceneIndex = value != null ? Scenes.IndexOf(value) + 1 : 0;
            OnPropertyChanged(nameof(CanGoPrevScene));
            OnPropertyChanged(nameof(CanGoNextScene));
            if (value != null)
                _ = LoadSceneAfterSelectionChangeAsync();
        }

        // Wrappers pour les deux fire-and-forget ci-dessus : les handlers de changement de propriete
        // sont void (impose par le generateur de source), pas moyen d'y faire un vrai await. Sans ce
        // wrapper, une erreur ici (DB, creation de lecteur...) disparaissait silencieusement au lieu
        // de remonter a l'utilisateur.
        private async Task LoadScenesAfterSessionChangeAsync(int sessionId)
        {
            try
            {
                await LoadScenesAsync(sessionId);
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        private async Task LoadSceneAfterSelectionChangeAsync()
        {
            try
            {
                await LoadScene();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        private async Task LoadScenesAsync(int sessionId)
        {
            var list = await _sceneDataService.GetScenesAsync(sessionId);
            Scenes = new ObservableCollection<Scene>(list);
            SceneCount = list.Count;
            SelectedScene = Scenes.FirstOrDefault();
            OnPropertyChanged(nameof(CanGoPrevScene));
            OnPropertyChanged(nameof(CanGoNextScene));
            await RefreshChapterBoundaryFlagsAsync();
        }

        /// <summary>
        /// Vérifie si le chapitre précédent/suivant SelectedSession (dans Sessions) a au moins une
        /// scène, pour piloter CanGoPrevScene/CanGoNextScene au-delà des limites du chapitre
        /// courant. À rappeler chaque fois que Sessions ou SelectedSession changent (nouveau
        /// chapitre chargé) - jamais de recherche en cascade au-delà du voisin immédiat.
        /// </summary>
        private async Task RefreshChapterBoundaryFlagsAsync()
        {
            var sessionIndex = SelectedSession != null ? Sessions.IndexOf(SelectedSession) : -1;

            _hasPreviousChapterScene = sessionIndex > 0
                && (await _sceneDataService.GetScenesAsync(Sessions[sessionIndex - 1].Id)).Count > 0;
            _hasNextChapterScene = sessionIndex >= 0 && sessionIndex + 1 < Sessions.Count
                && (await _sceneDataService.GetScenesAsync(Sessions[sessionIndex + 1].Id)).Count > 0;

            OnPropertyChanged(nameof(CanGoPrevScene));
            OnPropertyChanged(nameof(CanGoNextScene));
        }

        private async Task SaveCurrentSceneAsync()
        {
            // Cf. DebouncedSaveChannel : on ne persiste que les réglages du strip, jamais
            // l'AutoPlay (réglage explicite, qui serait sinon écrasé par l'état de lecture).
            var tasks = CurrentChannels
                .Where(c => c.SceneTrackId > 0)
                .Select(c => _sceneDataService.UpdateSceneTrackSettingsAsync(
                    c.SceneTrackId, c.Volume, c.IsLooping, c.IsFadeIn, c.IsFadeOut));
            await Task.WhenAll(tasks);
        }

        /// <summary>
        /// Appelé juste après avoir navigué vers AudioMixerPage (cf. CampaignViewModel.Launch).
        /// Sa propre préparation (sauvegarde, chargement chapitre/scène) ET celle de LoadScene
        /// tournent sous le MÊME Loading.RunAsync continu (au lieu d'appeler LoadScene() qui ouvre
        /// son propre empan Show/Hide séparé) : sans ça, IsLoading retombe à false entre les deux
        /// puis repasse à true, ce qui fait clignoter l'overlay - visible uniquement depuis ce
        /// chemin (l'accordéon), jamais lors d'un changement de scène classique dans le mixer.
        /// </summary>
        public async Task LoadFromPlayAsync(Campaign campaign, Session session, Scene scene)
        {
            if (_isLoadingScene) return;
            _isLoadingScene = true;

            var shell = Shell.Current;
            if (shell != null) shell.IsEnabled = false;

            try
            {
                List<SceneTrack> playable = new();

                await Loading.RunAsync(async () =>
                {
                    await SaveCurrentSceneAsync();

                    // Charger les chapitres de la campagne
                    var sessionList = await _sceneDataService.GetSessionsAsync(campaign.Id);
                    Sessions = new ObservableCollection<Session>(sessionList);

                    // Charger les scènes du chapitre sans déclencher les handlers de cascade
                    var sceneList = await _sceneDataService.GetScenesAsync(session.Id);
                    Scenes = new ObservableCollection<Scene>(sceneList);
                    SceneCount = sceneList.Count;

                    // Matcher par Id pour que le Picker trouve l'instance dans la collection
                    var matchedSession = Sessions.FirstOrDefault(s => s.Id == session.Id) ?? session;
                    var matchedScene = Scenes.FirstOrDefault(s => s.Id == scene.Id) ?? scene;

                    _suppressHandlers = true;
                    SelectedSession = matchedSession;
                    SelectedScene = matchedScene;
                    _suppressHandlers = false;
                    IsFreeformActive = false;

                    SceneIndex = Scenes.IndexOf(matchedScene) + 1;
                    OnPropertyChanged(nameof(CanGoPrevScene));
                    OnPropertyChanged(nameof(CanGoNextScene));
                    await RefreshChapterBoundaryFlagsAsync();

                    playable = await PrepareSceneTracksAsync(matchedScene);
                });

                await PopulateChannelsAsync(playable);
            }
            finally
            {
                if (shell != null) shell.IsEnabled = true;
                _isLoadingScene = false;
            }
        }

        public bool IsActiveScene(int sceneId) => _activeScene?.Id == sceneId;
        public bool IsActiveSession(int sessionId) => _activeScene?.SessionId == sessionId;
        public bool IsActiveCampaign(int campaignId) => SelectedSession?.CampaignId == campaignId;

        // Positionné par CampaignViewModel.Launch juste avant de naviguer vers le Mixer (donc
        // toujours AVANT qu'AudioMixerPage.OnAppearing ne s'exécute, pas de course possible) :
        // sans ça, le chargement automatique de la scène orpheline sur un Mixer vide (cf.
        // AudioMixerPage.OnAppearing) pourrait chevaucher le chargement de la vraie scène lancée,
        // et ce dernier serait silencieusement ignoré (bloqué par _isLoadingScene du premier).
        public bool SuppressNextFreeformAutoLoad { get; set; }

        /// <summary>
        /// Réinitialise le mixer quand la campagne/chapitre/scène affichée vient d'être supprimée
        /// ailleurs (page Campagnes) : libère les players et vide le sélecteur au lieu de laisser le
        /// mixer pointer sur des données qui n'existent plus en base.
        /// </summary>
        public void ResetActiveScene()
        {
            ClearChannels();
            _activeScene = null;
            OnPropertyChanged(nameof(HasActiveScene));

            _suppressHandlers = true;
            SelectedScene = null;
            SelectedSession = null;
            _suppressHandlers = false;
            IsFreeformActive = false;

            Scenes.Clear();
            Sessions.Clear();
            SceneCount = 0;
            SceneIndex = 1;
            _hasPreviousChapterScene = false;
            _hasNextChapterScene = false;
            OnPropertyChanged(nameof(CanGoPrevScene));
            OnPropertyChanged(nameof(CanGoNextScene));
        }

        [RelayCommand]
        public async Task SelectCampaign()
        {
            var campaigns = await _sceneDataService.GetCampaignsAsync();
            if (campaigns.Count == 0)
            {
                await ShowInfoAsync(Loc["ErrorTitle"], Loc["ErrorNoCampaigns"]);
                return;
            }

            var index = await ShowActionSheetIndexAsync(Loc["MixerCampaign"], campaigns.Select(c => c.Title).ToArray());
            if (index < 0) return;

            var candidate = campaigns[index];

            // Même logique que SelectSession pour les chapitres : on vérifie avant de peupler
            // Sessions, pour ne pas enchaîner sur un sélecteur de chapitre vide.
            var sessions = await _sceneDataService.GetSessionsAsync(candidate.Id);
            if (sessions.Count == 0)
            {
                await ShowInfoAsync(Loc["ErrorTitle"], Loc["ErrorCampaignHasNoChapters"]);
                return;
            }

            Sessions = new ObservableCollection<Session>(sessions);
            await SelectSession();
        }

        [RelayCommand]
        public async Task SelectSession()
        {
            // Sessions n'est peuplée qu'après le choix d'une campagne : en freeform tout juste
            // ouvert (aucune campagne encore chargée dans cette session d'appli), on enchaîne
            // d'abord sur SelectCampaign plutôt que de ne rien faire — celui-ci repeuple Sessions
            // et rappelle SelectSession lui-même une fois la campagne choisie.
            if (!Sessions.Any())
            {
                await SelectCampaign();
                return;
            }
            // Sélection par index et non par titre : deux chapitres homonymes doivent rester
            // sélectionnables individuellement.
            var index = await ShowActionSheetIndexAsync(Loc["MixerChapter"], Sessions.Select(s => s.Title).ToArray());
            if (index < 0) return;

            var candidate = Sessions[index];

            // Un chapitre sans scène laisserait le mixer dans un état incohérent (chapitre
            // sélectionné mais aucune scène/piste à jouer) : on vérifie avant d'affecter
            // SelectedSession plutôt qu'après, pour que le chapitre précédent reste sélectionné
            // sans avoir à revenir dessus explicitement en cas de rejet.
            var scenes = await _sceneDataService.GetScenesAsync(candidate.Id);
            if (scenes.Count == 0)
            {
                await ShowInfoAsync(Loc["ErrorTitle"], Loc["ErrorChapterHasNoScenes"]);
                return;
            }

            // _suppressHandlers évite qu'OnSelectedSessionChanged ne recharge les scènes en double
            // (on vient de les récupérer ci-dessus) et ne présélectionne la première automatiquement :
            // on veut enchaîner directement sur le dialog de sélection de scène, comme si
            // l'utilisateur avait tapé sur le bouton scène juste après.
            _suppressHandlers = true;
            SelectedSession = candidate;
            Scenes = new ObservableCollection<Scene>(scenes);
            SceneCount = scenes.Count;
            _suppressHandlers = false;
            await RefreshChapterBoundaryFlagsAsync();

            await SelectScene();

            // L'utilisateur peut annuler le dialog (tap à côté, retour) : on retombe alors sur la
            // 1ère scène plutôt que de laisser le mixer sans scène sélectionnée du tout.
            if (SelectedScene == null)
                SelectedScene = Scenes.FirstOrDefault();
        }

        [RelayCommand]
        public async Task SelectScene()
        {
            // Scenes n'est peuplée qu'après un chapitre sélectionné : sans chapitre (freeform tout
            // juste ouvert, ou campagne déjà chargée mais aucun chapitre choisi), on enchaîne sur
            // SelectSession plutôt que de ne rien faire — le bouton Scène se comporte alors comme
            // le bouton Chapitre, peu importe lequel des deux a été cliqué.
            if (!Scenes.Any())
            {
                await SelectSession();
                return;
            }
            var index = await ShowActionSheetIndexAsync(Loc["MixerScene"], Scenes.Select(s => s.Title).ToArray());
            if (index >= 0) SelectedScene = Scenes[index];
        }

        [RelayCommand]
        public async Task PrevScene()
        {
            if (SceneIndex > 1)
            {
                SelectedScene = Scenes[SceneIndex - 2];
                return;
            }
            await GoToAdjacentChapterSceneAsync(direction: -1);
        }

        [RelayCommand]
        public async Task NextScene()
        {
            if (SceneIndex < SceneCount)
            {
                SelectedScene = Scenes[SceneIndex];
                return;
            }
            await GoToAdjacentChapterSceneAsync(direction: 1);
        }

        /// <summary>
        /// Franchit une limite de chapitre depuis PrevScene/NextScene : bascule sur la dernière
        /// scène du chapitre précédent (direction -1) ou la première du chapitre suivant
        /// (direction +1). Ne fait rien si ce chapitre n'existe pas ou n'a aucune scène - cf.
        /// RefreshChapterBoundaryFlagsAsync, qui garde CanGoPrevScene/CanGoNextScene cohérents avec
        /// cette même condition pour que la flèche soit désactivée dans ce cas plutôt que de
        /// sembler active sans rien faire.
        /// </summary>
        private async Task GoToAdjacentChapterSceneAsync(int direction)
        {
            if (SelectedSession == null) return;
            var sessionIndex = Sessions.IndexOf(SelectedSession);
            var targetIndex = sessionIndex + direction;
            if (sessionIndex < 0 || targetIndex < 0 || targetIndex >= Sessions.Count) return;

            var targetSession = Sessions[targetIndex];
            var targetScenes = await _sceneDataService.GetScenesAsync(targetSession.Id);
            if (targetScenes.Count == 0) return;

            _suppressHandlers = true;
            SelectedSession = targetSession;
            Scenes = new ObservableCollection<Scene>(targetScenes);
            SceneCount = targetScenes.Count;
            _suppressHandlers = false;
            await RefreshChapterBoundaryFlagsAsync();

            SelectedScene = direction > 0 ? targetScenes[0] : targetScenes[^1];
        }

        // Empêche deux chargements de scène concurrents (l'UI est bloquée pendant le chargement,
        // mais LoadScene peut aussi être déclenché programmatiquement via LoadFromPlayAsync).
        private bool _isLoadingScene;

        [RelayCommand]
        public async Task LoadScene()
        {
            if (SelectedScene == null || _isLoadingScene) return;
            _isLoadingScene = true;
            IsFreeformActive = false;

            // Bloque toute la navigation (onglets, changement de scène...) pendant la (re)création
            // des channel strips : changer de scène ou d'onglet en plein chargement laisserait des
            // players orphelins ou un état de mixer incohérent.
            var shell = Shell.Current;
            if (shell != null) shell.IsEnabled = false;

            try
            {
                List<SceneTrack> playable = new();

                // L'overlay plein écran (Loading.IsLoading) ne couvre que la préparation - pas la
                // création des lecteurs, potentiellement coûteuse par piste sur Android (cf.
                // AudioMixerService.CreatePlayerAsync) - sinon les strips resteraient invisibles
                // derrière le spinner jusqu'à ce que TOUS les lecteurs soient prêts.
                await Loading.RunAsync(async () => { playable = await PrepareSceneTracksAsync(SelectedScene); });

                await PopulateChannelsAsync(playable);
            }
            finally
            {
                if (shell != null) shell.IsEnabled = true;
                _isLoadingScene = false;
            }
        }

        /// <summary>
        /// Sauvegarde/fade out/vide les channels actuels et renvoie les pistes jouables de
        /// <paramref name="scene"/>. Appelé sous Loading.RunAsync par LoadScene, LoadFromPlayAsync
        /// ET SelectFreeformScene : dans tous les cas ce doit être le MÊME empan Show/Hide que la
        /// préparation qui précède (sans quoi IsLoading retombe à false puis repasse à true entre
        /// les deux, et l'overlay clignote au lieu de rester affiché en continu).
        /// </summary>
        private async Task<List<SceneTrack>> PrepareSceneTracksAsync(Scene scene)
        {
            await SaveCurrentSceneAsync();

            var fadeTasks = CurrentChannels.Where(c => c.IsPlaying).Select(c => c.FadeOut()).ToArray();
            await Task.WhenAll(fadeTasks);

            ClearChannels();

            _activeScene = scene;
            OnPropertyChanged(nameof(HasActiveScene));

            var sceneTracks = await _sceneDataService.GetSceneTracksAsync(scene.Id);
            // Une piste illisible (fichier corrompu, verrouillé...) est simplement ignorée au lieu
            // de faire échouer toute la scène.
            return sceneTracks.Where(st => File.Exists(st.Track.FilePath)).ToList();
        }

        // Sans scène chargée (ni réelle, ni la scène orpheline), AddChannel produirait un strip
        // dont le picker de piste ne pourrait jamais persister (SaveChannelAsSceneTrack exige
        // _activeScene) : le bouton "+" du Mixer se lie à ce flag plutôt que de laisser
        // l'utilisateur découvrir le problème après coup, silencieusement.
        public bool HasActiveScene => _activeScene != null;

        /// <summary>
        /// Bascule le Mixer sur la scène orpheline (sans campagne/chapitre parent, cf.
        /// SceneDataService.GetOrCreateOrphanSceneAsync) : permet de l'utiliser en dehors de toute
        /// campagne. Une seule scène orpheline, créée au premier besoin puis réutilisée telle
        /// quelle - son contenu survit d'une utilisation à l'autre comme une vraie scène. Se
        /// comporte comme n'importe quel changement de scène (coupe/fade ce qui joue) : pas
        /// d'overlay sans interruption pour l'instant, cf. discussion avec l'utilisateur.
        /// </summary>
        [RelayCommand]
        public async Task SelectFreeformScene()
        {
            if (_isLoadingScene || IsFreeformActive) return;
            _isLoadingScene = true;

            var shell = Shell.Current;
            if (shell != null) shell.IsEnabled = false;

            try
            {
                List<SceneTrack> playable = new();

                await Loading.RunAsync(async () =>
                {
                    var orphanScene = await _sceneDataService.GetOrCreateOrphanSceneAsync();

                    // Sessions n'est PAS vidée : si une campagne était déjà chargée, le sélecteur
                    // "Chapitre" reste utilisable pour y revenir sans repasser par l'accordéon.
                    _suppressHandlers = true;
                    SelectedSession = null;
                    SelectedScene = null;
                    _suppressHandlers = false;
                    IsFreeformActive = true;

                    Scenes.Clear();
                    SceneCount = 0;
                    SceneIndex = 0;
                    // SelectedSession vient d'être remis à null (Sessions elle-même n'est pas
                    // vidée) : pas de chapitre voisin à considérer tant qu'on est en freeform.
                    _hasPreviousChapterScene = false;
                    _hasNextChapterScene = false;
                    OnPropertyChanged(nameof(CanGoPrevScene));
                    OnPropertyChanged(nameof(CanGoNextScene));

                    playable = await PrepareSceneTracksAsync(orphanScene);
                });

                await PopulateChannelsAsync(playable);
            }
            finally
            {
                if (shell != null) shell.IsEnabled = true;
                _isLoadingScene = false;
            }
        }
    }
}
