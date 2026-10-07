using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using Resonance.Features.Library;
using Resonance.Models.Library;
using Resonance.Services;

namespace Resonance.Components.Dialogs
{
    /// <summary>
    /// Logique du formulaire de création/édition d'une piste - TrackEditDialog n'est qu'un wrapper
    /// XAML lié dessus, comme toute page de l'appli (pas de logique en code-behind). Ne persiste
    /// rien tant que Save n'est pas appelé : dédup par hash, copie locale du fichier et sauvegarde
    /// se font toutes dans SaveAsync, une fois le dialogue confirmé.
    /// </summary>
    public partial class TrackEditDialogViewModel : DialogViewModel<bool>
    {
        private readonly ILibraryDataService _libraryDataService;
        private readonly FileService _fileService;
        private readonly AudioPlayerService _audioPlayerService;
        private readonly List<string> _categories;

        private byte[]? _pendingCoverBytes;
        private bool _filePicked;

        protected override bool CancelResult => false;

        public Track Item { get; }

        [ObservableProperty]
        private string title = string.Empty;

        [ObservableProperty]
        private string name = string.Empty;

        [ObservableProperty]
        private string fileButtonText = string.Empty;

        [ObservableProperty]
        private string categoryButtonText = string.Empty;

        [ObservableProperty]
        private string durationText = string.Empty;

        public TrackEditDialogViewModel(
            Track item,
            List<string> categories,
            ILibraryDataService libraryDataService,
            FileService fileService,
            AudioPlayerService audioPlayerService)
        {
            Item = item;
            _categories = categories;
            _libraryDataService = libraryDataService;
            _fileService = fileService;
            _audioPlayerService = audioPlayerService;

            Title = Loc[item.Id != 0 ? "TrackEditTitle" : "TrackCreateTitle"];
            Name = item.Title;
            UpdateFileButtonText();
            UpdateCategoryButtonText();
            UpdateDurationText();
        }

        private void UpdateFileButtonText() =>
            FileButtonText = string.IsNullOrEmpty(Item.FilePath) ? Loc["TrackSelectFile"] : Path.GetFileName(Item.FilePath);

        private void UpdateCategoryButtonText() =>
            CategoryButtonText = string.IsNullOrEmpty(Item.Category) ? Loc["LibImportNoCategory"] : Item.Category;

        private void UpdateDurationText() =>
            DurationText = Item.DurationFormatted;

        [RelayCommand]
        public async Task PickFile()
        {
            try
            {
                var result = await _fileService.PickAudioFileAsync(Loc["TrackSelectFile"]);
                if (result == null)
                    return;

                // Lecture des tags hors du thread UI : parser un gros fichier en synchrone gelait
                // l'interface (risque d'ANR sur Android).
                var (title, duration, coverBytes) = await Task.Run(() =>
                {
                    var tagfile = TagLib.File.Create(result.FullPath);
                    // Sans titre taggé, ExtractTitle retombe sur ce nom : sans extension, pour ne
                    // pas pré-remplir le champ Titre avec "ma-piste.mp3".
                    return (TrackTagHelper.ExtractTitle(tagfile.Tag, Path.GetFileNameWithoutExtension(result.FileName)),
                            tagfile.Properties.Duration,
                            CoverArtService.ExtractCoverThumbnailBytes(tagfile.Tag));
                });

                // Chemin brut du fichier choisi (pas encore copié en local) : permet déjà la
                // prévisualisation via le bouton lecture de TrackButtonView.
                Item.FilePath = result.FullPath;
                Item.Title = title;
                Item.Duration = duration;
                _pendingCoverBytes = coverBytes;
                _filePicked = true;

                Name = title;
                UpdateFileButtonText();
                UpdateDurationText();
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
        }

        [RelayCommand]
        public async Task SelectCategory()
        {
            var options = new[] { Loc["LibImportNoCategory"] }.Concat(_categories).ToArray();
            var index = await ShowActionSheetIndexAsync(Loc["LibImportCategoryTitle"], options);
            if (index < 0)
                return;

            Item.Category = index == 0 ? string.Empty : options[index];
            UpdateCategoryButtonText();
        }

        [RelayCommand]
        public async Task Save()
        {
            Item.Title = Name.Trim();

            // Un fichier a été choisi (Item.FilePath pointe encore vers le fichier brut, pas
            // copié) : même dédup par hash + copie que l'import multiple, avant de remplacer
            // FilePath par le chemin local définitif. Sans nouveau fichier (simple édition du
            // titre/de la catégorie), FilePath/Hash/ImagePath restent inchangés.
            if (_filePicked)
            {
                var pickedPath = Item.FilePath;
                var hash = await Task.Run(() => TrackTagHelper.ComputeSha256(pickedPath));
                var existing = await _libraryDataService.FindTrackByHashAsync(hash, Item.Id);

                Item.FilePath = existing != null
                    ? existing.FilePath
                    : await Task.Run(() => _fileService.CopyTrackToLocal(pickedPath));
                Item.Hash = hash;

                // Dédup : réutilise la pochette déjà extraite pour ce fichier plutôt que d'en
                // écrire une seconde copie identique sur disque.
                Item.ImagePath = existing != null && !string.IsNullOrEmpty(existing.ImagePath)
                    ? existing.ImagePath
                    : _pendingCoverBytes != null ? _fileService.SaveCoverThumbnail(_pendingCoverBytes) : string.Empty;

                // Nettoie la copie laissée par FilePicker dans le cache Android maintenant que le
                // fichier est traité (copié en local ou dédupliqué) - sinon elle reste orpheline.
                _fileService.DeleteIfCached(pickedPath);
            }

            await _libraryDataService.SaveLibraryItemAsync(Item);
            WeakReferenceMessenger.Default.Send(new LibraryUpdatedMessage());

            Close(true);
        }

        public void StopAudio() => _audioPlayerService.Stop();
    }
}
