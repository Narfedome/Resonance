using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Components;
using Resonance.Components.Dialogs;
using Resonance.Models.Library;
using Resonance.Services;
using System.Collections.ObjectModel;

namespace Resonance.Features.Library
{
    public partial class LibraryTrackViewModel : BaseViewModel
    {
        // Sur Windows, si une page ne suffit pas à remplir le viewport (donc rien à scroller pour en
        // charger plus), LibraryTrackView.xaml.cs recharge automatiquement des pages supplémentaires
        // (ScheduleViewportFillCheck) - pas besoin d'une valeur artificiellement grande ici.
        private const int PageSize = 15;

        private readonly ILibraryPickerNavigationService _navigation;
        private readonly ILibraryDataService _libraryDataService;
        private readonly FileService _fileService;
        private readonly AudioPlayerService _audioPlayerService;

        private int _loadedCount;
        private bool _hasMoreItems = true;
        private bool _isLoadingMore;
        private bool _suppressCategoryReload;
        private string? _categoryFilter;

        // Verrou couvrant toute mutation de l'état de pagination (_loadedCount/_hasMoreItems/
        // TrackItems) : ReloadAsync (vidage + rechargement, déclenché par changement de catégorie/
        // InitializeAsync/LibraryUpdatedMessage) et LoadNextPageAsync (ajout d'une page, déclenché par
        // le scroll - cf. LibraryTrackView.xaml.cs) sont deux chemins indépendants vers cet état
        // partagé. Sans lui, un scroll qui arriverait pendant qu'un ReloadAsync est au milieu de son
        // ClearTrackItems() courrait avec lui - état de pagination incohérent.
        private readonly SemaphoreSlim _loadGate = new(1, 1);

        // Permet d'interrompre un chargement en cours quand la page quitte l'écran (changement
        // d'onglet, cf. LibraryTrackPage/LibraryTrackSelectorPage.OnDisappearing) : sans ça, un
        // chargement démarré juste avant de quitter l'onglet (ex. juste après un gros import)
        // continuait à solliciter le thread UI/la DB en arrière-plan pendant qu'aucune tuile n'est
        // visible pour en profiter, au détriment de l'onglet réellement affiché. La requête DB
        // elle-même n'est pas interrompue (sqlite-net-pcl ne le permet pas), seule la boucle qui
        // ajoute les tuiles ensuite l'est. EnsureFreshLoadToken recrée le jeton s'il a déjà été
        // annulé, pour qu'un nouveau chargement (retour sur l'onglet, scroll...) reparte normalement.
        private CancellationTokenSource _loadCts = new();

        public void CancelPendingLoad() => _loadCts.Cancel();

        private CancellationToken EnsureFreshLoadToken()
        {
            if (_loadCts.IsCancellationRequested)
            {
                _loadCts.Dispose();
                _loadCts = new CancellationTokenSource();
            }

            return _loadCts.Token;
        }

        public LibraryMultiSelection<Track> Selection { get; } = new();

        public bool HasSelection => Selection.HasSelection;

        public string SelectedCountLabel => string.Format(Loc["LibSelectedCount"], Selection.SelectedCount);

        public bool CanDelete => HasSelection || SelectedTrackItem != null;

        public string DeleteTooltip => HasSelection ? SelectedCountLabel : Loc["LibDelete"];

        private string AllCategoriesLabel => Loc["LibAllCategories"];

        // Réutilise le même libellé que le choix "Aucun dossier" du dialogue d'import (PickImportCategoryAsync) :
        // catégorie native représentant Track.Category vide, non stockée en CategoryEntity, non
        // renommable/supprimable (cf. CategoryListViewModel).
        private string NoFolderLabel => Loc["LibImportNoCategory"];

        [ObservableProperty]
        public ObservableCollection<TrackGroup> trackItems = new();

        // TrackItems.Count compte les groupes (catégories), pas les pistes - utilisé par les
        // heuristiques de pagination (seuil de scroll, remplissage de viewport, cf.
        // LibraryTrackView.xaml.cs) qui doivent comparer un nombre de pistes chargées, pas de groupes.
        public int LoadedTrackCount => TrackItems.Sum(g => g.Count);

        public bool HasTrackItems => LoadedTrackCount > 0;

        // La CollectionView reste groupée en permanence (structure interne toujours en TrackGroup,
        // même un seul groupe en filtré) plutôt que de jongler entre deux formes de données selon le
        // filtre : seul l'en-tête de section est masqué hors du filtre "Tout", où il serait redondant
        // avec le bouton de filtre déjà affiché juste au-dessus.
        public bool ShowGroupHeaders => SelectedCategory == AllCategoriesLabel;

        // Deux variantes de l'état vide : "Tout" vide (aucune piste importée du tout) propose les
        // raccourcis d'import ; une catégorie spécifique vide (le filtre ne matche juste rien) affiche
        // un simple message, l'import n'y étant pas forcément pertinent (une piste importée n'atterrit
        // pas nécessairement dans cette catégorie). _hasLoadedOnce : avant le tout premier chargement,
        // SelectedCategory vaut encore "" (default), différent de AllCategoriesLabel - sans ce garde,
        // ShowEmptyCategoryMessage s'affichait un instant au tout premier rendu de la page, avant même
        // qu'InitializeAsync (appelé depuis OnNavigatedTo, donc après ce premier rendu) n'ait eu la
        // main pour faire passer Loading.IsLoading à true.
        public bool ShowEmptyLibraryCard => _hasLoadedOnce && !HasTrackItems && SelectedCategory == AllCategoriesLabel;

        public bool ShowEmptyCategoryMessage => _hasLoadedOnce && !HasTrackItems && SelectedCategory != AllCategoriesLabel;

        [ObservableProperty]
        private Track? selectedTrackItem;

        partial void OnSelectedTrackItemChanged(Track? oldValue, Track? newValue)
        {
            // Pilote la bordure de mise en évidence à la main (cf. LibraryItem.IsPrimarySelected) :
            // le CollectionView n'utilise plus la sélection native (SelectionMode="None"), pour
            // éviter le fond de sélection natif Android qui reste teinté par colorAccent.
            if (oldValue != null) oldValue.IsPrimarySelected = false;
            if (newValue != null) newValue.IsPrimarySelected = true;

            OnPropertyChanged(nameof(CanDelete));
            OnPropertyChanged(nameof(DeleteTooltip));
        }

        [ObservableProperty]
        private bool isLoadingMore;

        [ObservableProperty]
        private ObservableCollection<string> categories = new();

        [ObservableProperty]
        private string selectedCategory = string.Empty;

        partial void OnSelectedCategoryChanged(string value)
        {
            OnPropertyChanged(nameof(ShowEmptyLibraryCard));
            OnPropertyChanged(nameof(ShowEmptyCategoryMessage));
            OnPropertyChanged(nameof(ShowGroupHeaders));

            if (_suppressCategoryReload)
                return;

            _ = ReloadAfterCategoryChangeAsync();
        }

        // Le handler de changement de propriete est void (impose par le generateur de source) : pas
        // moyen d'y faire un vrai await. Sans ce wrapper, une erreur DB dans ReloadAsync disparaissait
        // silencieusement (tache fire-and-forget jamais observee) au lieu de remonter a l'utilisateur.
        private async Task ReloadAfterCategoryChangeAsync()
        {
            try
            {
                await ReloadAsync();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        public LibraryTrackViewModel(ILibraryPickerNavigationService navigation, ILibraryDataService libraryDataService, AudioPlayerService audioPlayerService, FileService fileService)
        {
            _navigation = navigation;
            _libraryDataService = libraryDataService;
            _audioPlayerService = audioPlayerService;
            _fileService = fileService;
            TrackItems.CollectionChanged += (_, _) =>
            {
                OnPropertyChanged(nameof(HasTrackItems));
                OnPropertyChanged(nameof(ShowEmptyLibraryCard));
                OnPropertyChanged(nameof(ShowEmptyCategoryMessage));
            };
            Selection.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(Selection.SelectedCount))
                {
                    OnPropertyChanged(nameof(HasSelection));
                    OnPropertyChanged(nameof(SelectedCountLabel));
                    OnPropertyChanged(nameof(CanDelete));
                    OnPropertyChanged(nameof(DeleteTooltip));
                }
            };
            WeakReferenceMessenger.Default.Register<LibraryUpdatedMessage>(this,
            async (r, m) =>
            {
                await MainThread.InvokeOnMainThreadAsync(async () =>
                {
                    // Sans Loading.RunAsync, ce rechargement (déclenché après un import/edit, souvent
                    // pendant que l'onglet Bibliothèque n'est même pas affiché) viderait TrackItems
                    // puis le repeuplerait sans jamais activer le spinner - la carte "bibliothèque
                    // vide" pouvait alors s'afficher un instant (ou plus, sur un gros import) à la
                    // place.
                    await Loading.RunAsync(async () =>
                    {
                        await RefreshCategoriesAsync();
                        await ReloadAsync();
                    });
                });
            });
        }

        // Stop any playing audio when leaving the view
        public void StopAudio()
        {
            _audioPlayerService?.Stop();
        }

        private bool _isInitializing;
        private bool _hasLoadedOnce;

        public async Task InitializeAsync()
        {
            // Le premier chargement (requête catégories + page de pistes + extraction des pochettes)
            // peut être long : pas la peine de le refaire à chaque retour sur l'onglet, alors que
            // LibraryUpdatedMessage (édition, catégories...) recharge déjà la liste quand les données
            // changent réellement. Ça évite aussi de revivre ce chargement long à chaque changement
            // d'onglet.
            if (_hasLoadedOnce)
                return;

            // Évite un rechargement concurrent si l'utilisateur quitte puis revient sur l'onglet avant
            // la fin du chargement précédent (navigation restant possible pendant le chargement) - on
            // laisse simplement le chargement déjà en cours se terminer plutôt que d'en démarrer un
            // second en parallèle, qui corromprait l'état de la liste (doublons, sélection incohérente).
            if (_isInitializing)
                return;

            _isInitializing = true;
            try
            {
                await Loading.RunAsync(async () =>
                {
                    await RefreshCategoriesAsync();
                    await ReloadAsync();
                });
                _hasLoadedOnce = true;
                OnPropertyChanged(nameof(ShowEmptyLibraryCard));
                OnPropertyChanged(nameof(ShowEmptyCategoryMessage));
            }
            finally
            {
                _isInitializing = false;
            }
        }

        private async Task RefreshCategoriesAsync()
        {
            var existing = await _libraryDataService.GetCategoryNamesAsync(typeof(Track));
            var previousSelection = string.IsNullOrEmpty(SelectedCategory) ? AllCategoriesLabel : SelectedCategory;

            _suppressCategoryReload = true;

            Categories.Clear();
            Categories.Add(AllCategoriesLabel);
            Categories.Add(NoFolderLabel);
            foreach (var c in existing)
                Categories.Add(c);

            SelectedCategory = Categories.Contains(previousSelection) ? previousSelection : AllCategoriesLabel;

            _suppressCategoryReload = false;
        }

        private bool _isReloading;
        private bool _reloadPending;

        // ReloadAsync a plusieurs déclencheurs indépendants (changement de catégorie, message
        // LibraryUpdatedMessage après import, InitializeAsync) qui peuvent se chevaucher. Sans ce
        // garde-fou, un second ReloadAsync concurrent viderait/reconstruirait TrackItems PENDANT que
        // le premier est encore en train de le peupler. On sérialise donc les appels : un appel déjà
        // en cours absorbe les suivants et relance une passe (avec l'état le plus à jour, ex. la
        // dernière catégorie choisie) juste après, plutôt que de les laisser s'exécuter en parallèle.
        private async Task ReloadAsync()
        {
            if (_isReloading)
            {
                _reloadPending = true;
                return;
            }

            _isReloading = true;
            try
            {
                do
                {
                    _reloadPending = false;
                    await ReloadCoreAsync();
                } while (_reloadPending);
            }
            finally
            {
                _isReloading = false;
            }
        }

        private async Task ReloadCoreAsync()
        {
            // Tient _loadGate pendant tout le reset + premier chargement de page : un LoadNextPageAsync
            // externe (scroll - cf. LibraryTrackView.xaml.cs) qui arriverait pendant ce temps attend
            // simplement que le verrou se libère au lieu de muter _loadedCount/TrackItems en même temps
            // que ce reset (cf. commentaire sur _loadGate).
            await _loadGate.WaitAsync();
            try
            {
                // Un rechargement complet (changement de catégorie, LibraryUpdatedMessage...) repart
                // toujours d'un jeton frais, y compris si le précédent avait été annulé par un départ
                // d'onglet entre-temps.
                var token = EnsureFreshLoadToken();

                // null = pas de filtre (Tout), "" = uniquement les pistes sans catégorie (Aucun
                // dossier), sinon le nom exact de la catégorie choisie - cf. GetItemsPageAsync.
                _categoryFilter = SelectedCategory == AllCategoriesLabel ? null
                    : SelectedCategory == NoFolderLabel ? string.Empty
                    : SelectedCategory;

                ClearTrackItems();
                _loadedCount = 0;
                _hasMoreItems = true;
                SelectedTrackItem = null;

                // Changer de catégorie change le contexte de sélection multiple : on repart de zéro
                // pour éviter de supprimer par erreur des pistes qu'on ne voit plus.
                Selection.Clear();

                // Appel direct de la version non verrouillée (pas LoadNextPageAsync) : le verrou est
                // déjà tenu ici, un SemaphoreSlim n'étant pas réentrant, ré-attendre dessus bloquerait
                // indéfiniment.
                await LoadNextPageCoreAsync(token);
            }
            finally
            {
                _loadGate.Release();
            }
        }

        private void ClearTrackItems()
        {
            foreach (var group in TrackItems)
                foreach (var t in group)
                    Selection.Untrack(t);

            TrackItems.Clear();
        }

        private string CategoryDisplayName(string category) => string.IsNullOrEmpty(category) ? NoFolderLabel : category;

        /// <summary>Ajoute une piste à la fin du groupe (catégorie) correspondant dans TrackItems, en
        /// créant ce groupe s'il n'existe pas encore. TrackItems.CollectionChanged (cf. constructeur)
        /// ne se déclenche que quand un groupe est ajouté/retiré, pas quand une piste s'ajoute à un
        /// groupe déjà présent - les notifications ci-dessous couvrent donc ce cas manquant.</summary>
        private void AddTrackToGroup(Track track)
        {
            var groupName = CategoryDisplayName(track.Category);
            var group = TrackItems.FirstOrDefault(g => g.Name == groupName);
            if (group == null)
            {
                group = new TrackGroup(groupName);
                TrackItems.Add(group);
            }

            group.Add(track);
            OnPropertyChanged(nameof(HasTrackItems));
            OnPropertyChanged(nameof(ShowEmptyLibraryCard));
            OnPropertyChanged(nameof(ShowEmptyCategoryMessage));
        }

        // Point d'entrée externe (scroll) : passe par _loadGate pour ne jamais s'exécuter pendant
        // qu'un ReloadCoreAsync est au milieu de son reset (cf. commentaire sur _loadGate).
        // ReloadCoreAsync, qui tient déjà le verrou, appelle directement LoadNextPageCoreAsync plutôt
        // que cette enveloppe, pour éviter un blocage sur un SemaphoreSlim non réentrant.
        private async Task LoadNextPageAsync()
        {
            await _loadGate.WaitAsync();
            try
            {
                var token = EnsureFreshLoadToken();
                await LoadNextPageCoreAsync(token);
            }
            finally
            {
                _loadGate.Release();
            }
        }

        private async Task LoadNextPageCoreAsync(CancellationToken cancellationToken)
        {
            if (_isLoadingMore || !_hasMoreItems)
                return;

            // Le spinner du footer (IsLoadingMore) ne doit apparaître qu'en pagination (scroll) - la
            // toute première page est déjà couverte par le spinner de la liste (Loading.IsLoading),
            // sans quoi les deux s'affichaient en même temps au premier chargement.
            var isPagination = _loadedCount > 0;

            _isLoadingMore = true;
            if (isPagination)
                IsLoadingMore = true;

            try
            {
                // La requête elle-même n'est pas annulable (sqlite-net-pcl ne le permet pas) : le
                // jeton n'est vérifié qu'à partir d'ici, dans la boucle qui peuple TrackItems.
                var items = await _libraryDataService.GetItemsPageAsync(typeof(Track), _loadedCount, PageSize, _categoryFilter);
                _hasMoreItems = items.Count == PageSize;

                // Ne précharge plus les pochettes ici : chaque tuile (TrackButtonViewModel) charge déjà
                // la sienne en tâche de fond dès qu'elle reçoit sa track, avec un placeholder pendant ce
                // temps (cf. TrackButtonView.xaml). Attendre ici bloquait l'apparition de toute la page
                // derrière la pochette la plus lente à extraire (repli TagLib pour une piste sans
                // ImagePath en cache, ex. legacy ou fraîchement importée) - les tuiles apparaissent
                // maintenant une à une au fil de la boucle ci-dessous, pochette à part.
                foreach (var item in items)
                {
                    // Vérifié avant chaque tuile plutôt qu'une seule fois avant la boucle : une page
                    // quittée (changement d'onglet) en plein milieu arrête d'en ajouter d'autres tout
                    // de suite, pas seulement à la prochaine page.
                    cancellationToken.ThrowIfCancellationRequested();

                    var track = (Track)item;
                    Selection.Track(track);

                    // La requête (GetItemsPageAsync) trie par catégorie : les pistes d'un même groupe
                    // sortent toujours consécutives, y compris à cheval sur deux pages successives -
                    // pas besoin de rechercher le groupe ailleurs que dans AddTrackToGroup.
                    AddTrackToGroup(track);

                    // Incrémenté ici (par piste) plutôt qu'une fois après la boucle : si une annulation
                    // interrompt la boucle en plein milieu, _loadedCount reflète alors exactement ce qui
                    // a déjà été ajouté, et un prochain chargement reprend au bon endroit sans doublon
                    // ni piste sautée.
                    _loadedCount++;

                    // Laisse la main au thread UI entre chaque tuile (traitement des messages Windows -
                    // dont les taps sur les autres onglets) plutôt que d'enchaîner les 12 ajouts et
                    // réalisations visuelles en un seul bloc synchrone ininterrompu.
                    await Task.Yield();
                }

                if (SelectedTrackItem == null)
                    SelectedTrackItem = TrackItems.FirstOrDefault()?.FirstOrDefault();
            }
            catch (OperationCanceledException)
            {
                // Page quittée pendant le chargement (changement d'onglet) : pas une vraie erreur à
                // remonter, juste un chargement abandonné en cours de route.
            }
            finally
            {
                _isLoadingMore = false;
                if (isPagination)
                    IsLoadingMore = false;
            }
        }

        [RelayCommand]
        public async Task LoadMoreTracks()
        {
            await LoadNextPageAsync();
        }

        [RelayCommand]
        public async Task ConfirmSelection()
        {
            if (SelectedTrackItem is Track trackItem)
                await _navigation.ClosePickerAsync(trackItem);
        }

        [RelayCommand]
        public async Task Cancel()
        {
            await _navigation.ClosePickerAsync(null);
        }

        [RelayCommand]
        public async Task SelectCategory()
        {
            var result = await ShowActionSheetAsync(Loc["LibImportCategoryTitle"], Categories.ToArray());
            if (result != null)
                SelectedCategory = result;
        }

        [RelayCommand]
        public async Task ManageCategories()
        {
            await Shell.Current.GoToAsync(nameof(CategoryListPage),
                new Dictionary<string, object> { { "LibraryType", typeof(Track) } });
        }

        [RelayCommand]
        public async Task SelectAll()
        {
            var ids = await _libraryDataService.GetItemIdsAsync(typeof(Track), _categoryFilter);
            var allTracks = TrackItems.SelectMany(g => g);

            // Bascule : si tout est déjà sélectionné, un nouvel appui tout désélectionne.
            if (ids.Count > 0 && Selection.ContainsAll(ids))
                Selection.DeselectAll(allTracks);
            else
                Selection.SelectIds(ids, allTracks);
        }

        /// <summary>
        /// Supprime la sélection multiple (cases cochées) si elle existe, sinon la piste actuellement
        /// tapée - un seul bouton couvre donc la suppression individuelle et la suppression multiple.
        /// </summary>
        [RelayCommand]
        public async Task DeleteSelectedItems()
        {
            var ids = Selection.HasSelection
                ? Selection.SelectedIds.ToList()
                : SelectedTrackItem != null ? new List<int> { SelectedTrackItem.Id } : new List<int>();

            if (ids.Count == 0)
                return;

            // Une seule piste ciblée (que ce soit via une case cochée ou l'item tapé) : même message
            // que la suppression individuelle, avec son titre plutôt qu'un simple compteur.
            var message = ids.Count == 1
                ? string.Format(Loc["DialogDeleteTrackConfirm"], (TrackItems.SelectMany(g => g).FirstOrDefault(t => t.Id == ids[0]) ?? SelectedTrackItem)?.Title)
                : string.Format(Loc["LibDeleteSelectedConfirm"], ids.Count);

            if (!await ConfirmAsync(Loc["DialogDelete"], message))
                return;

            StopAudio();

            var deleted = await _libraryDataService.DeleteItemsAsync(typeof(Track), ids);

            // Ne supprime les fichiers physiques (audio ET vignette de pochette) que s'ils ne sont
            // plus référencés par aucune autre track (dédup : plusieurs tracks peuvent partager le
            // même fichier et la même pochette).
            var referenced = await _libraryDataService.GetAllReferencedFilePathsAsync();
            foreach (var track in deleted.OfType<Track>())
            {
                if (!referenced.Contains(track.FilePath))
                    _fileService.DeleteTrackFromLocal(track.FilePath);
                if (!string.IsNullOrEmpty(track.ImagePath) && !referenced.Contains(track.ImagePath))
                    _fileService.DeleteTrackFromLocal(track.ImagePath);
            }

            foreach (var group in TrackItems.ToList())
            {
                foreach (var item in group.Where(t => ids.Contains(t.Id)).ToList())
                {
                    Selection.Untrack(item);
                    group.Remove(item);
                    _loadedCount--;
                }

                // Un groupe vidé de toutes ses pistes ne doit pas laisser un en-tête de catégorie
                // sans rien en dessous.
                if (group.Count == 0)
                    TrackItems.Remove(group);
            }

            Selection.Clear();
            SelectedTrackItem = TrackItems.FirstOrDefault()?.FirstOrDefault();

            await RefreshCategoriesAsync();
        }

        [RelayCommand]
        public async Task Edit()
        {
            if (SelectedTrackItem == null)
                return;

            await ShowTrackEditDialogAsync((Track)SelectedTrackItem.Clone());
        }

        [RelayCommand]
        public async Task Create()
        {
            await ShowTrackEditDialogAsync(new Track());
        }

        // La persistance (dédup par hash, copie locale, sauvegarde) vit dans
        // TrackEditDialogViewModel.Save, pas ici : ce ViewModel n'a plus qu'à afficher le dialogue.
        private async Task ShowTrackEditDialogAsync(Track item)
        {
            var categories = await _libraryDataService.GetCategoryNamesAsync(typeof(Track));
            var dialogViewModel = new TrackEditDialogViewModel(item, categories, _libraryDataService, _fileService, _audioPlayerService);
            await ShowDialogAsync(new TrackEditDialog(dialogViewModel));
        }

        [RelayCommand]
        public void SelectItem(Track track)
        {
            SelectedTrackItem = track;
        }

        [RelayCommand]
        public async Task ImportMultipleTracks()
        {
            var files = (await _fileService.PickAudioFilesAsync(Loc["TrackSelectFiles"]))?.ToList();
            if (files == null || files.Count == 0)
                return;

            var category = await PickImportCategoryAsync();
            if (category == null)
                return;

            int imported = 0;
            int duplicates = 0;
            int failed = 0;

            var popupView = new ImportProgressPopupView();
            popupView.ViewModel.Title = Loc["LibImportInProgress"];
            popupView.ViewModel.TotalCount = files.Count;

            var page = Shell.Current.CurrentPage;
            page.ShowPopup(popupView, new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = false });

            try
            {
                foreach (var file in files)
                {
                    popupView.ViewModel.CurrentFileName = file.FileName;

                    try
                    {
                        var hash = await Task.Run(() => TrackTagHelper.ComputeSha256(file.FullPath));
                        var existing = await _libraryDataService.FindTrackByHashAsync(hash, 0);

                        string filePath;
                        if (existing != null)
                        {
                            filePath = existing.FilePath;
                            duplicates++;
                        }
                        else
                        {
                            filePath = await Task.Run(() => _fileService.CopyTrackToLocal(file.FullPath));
                        }

                        var title = file.FileName;
                        var duration = TimeSpan.Zero;
                        byte[]? coverBytes = null;
                        try
                        {
                            var (tagTitle, tagDuration, cover) = await Task.Run(() =>
                            {
                                var tagfile = TagLib.File.Create(file.FullPath);
                                var t = TrackTagHelper.ExtractTitle(tagfile.Tag, file.FileName);
                                var c = CoverArtService.ExtractCoverThumbnailBytes(tagfile.Tag);
                                return (t, tagfile.Properties.Duration, c);
                            });
                            title = tagTitle;
                            duration = tagDuration;
                            coverBytes = cover;
                        }
                        catch { /* tags illisibles, on garde le nom de fichier */ }

                        // Dédup : réutilise la pochette déjà extraite pour ce fichier plutôt que
                        // d'en écrire une seconde copie identique sur disque.
                        var imagePath = existing != null && !string.IsNullOrEmpty(existing.ImagePath)
                            ? existing.ImagePath
                            : coverBytes != null ? _fileService.SaveCoverThumbnail(coverBytes) : string.Empty;

                        var track = new Track
                        {
                            Title = title,
                            FilePath = filePath,
                            ImagePath = imagePath,
                            Duration = duration,
                            Hash = hash,
                            Category = category
                        };
                        await _libraryDataService.SaveLibraryItemAsync(track);

                        // N'affiche la nouvelle track dans la liste que si elle correspond au filtre actif
                        if (_categoryFilter == null || string.Equals(track.Category, _categoryFilter, StringComparison.OrdinalIgnoreCase))
                        {
                            Selection.Track(track);
                            AddTrackToGroup(track);
                            _loadedCount++;
                        }

                        imported++;
                    }
                    catch { failed++; /* fichier invalide, on passe au suivant */ }
                    finally
                    {
                        // Nettoie la copie laissée par FilePicker dans le cache Android une fois le fichier
                        // traité (copié en local ou dédupliqué) - sinon elle reste orpheline indéfiniment.
                        _fileService.DeleteIfCached(file.FullPath);
                    }

                    popupView.ViewModel.ProcessedCount++;
                }
            }
            finally
            {
                await page.ClosePopupAsync();
            }

            await RefreshCategoriesAsync();

            // Les fichiers en échec (illisibles, corrompus...) sont signalés plutôt que passés
            // sous silence : sans ça l'utilisateur ne savait pas pourquoi il manquait des pistes.
            var resultMessage = string.Format(Loc["LibImportResult"], imported, duplicates);
            if (failed > 0)
                resultMessage += "\n" + string.Format(Loc["LibImportResultErrors"], failed);
            await ShowInfoAsync(Loc["LibImport"], resultMessage);
        }

        /// <summary>
        /// Retourne null si l'utilisateur annule (l'import complet doit alors être abandonné),
        /// ou la catégorie choisie (chaîne vide si "Aucun dossier").
        /// </summary>
        private async Task<string?> PickImportCategoryAsync()
        {
            var existing = await _libraryDataService.GetCategoryNamesAsync(typeof(Track));
            var options = new[] { Loc["LibImportNoCategory"] }
                .Concat(existing)
                .Append(Loc["LibImportNewCategory"])
                .ToArray();

            var choice = await ShowActionSheetAsync(Loc["LibImportCategoryTitle"], options);
            if (choice == null)
                return null;

            if (choice == Loc["LibImportNoCategory"])
                return string.Empty;

            if (choice != Loc["LibImportNewCategory"])
                return choice;

            var newCategory = await ShowPromptAsync(Loc["LibImportNewCategory"], Loc["LibImportNewCategoryPrompt"]);
            if (string.IsNullOrWhiteSpace(newCategory))
                return string.Empty;

            var trimmed = newCategory.Trim();
            await _libraryDataService.EnsureCategoryAsync(typeof(Track), trimmed);
            return trimmed;
        }
    }
}
