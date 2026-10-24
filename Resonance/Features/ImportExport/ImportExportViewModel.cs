using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Resonance.Components;
using Resonance.Models;
using Resonance.Models.ImportExport;
using Resonance.Services;
using System.IO.Pipelines;

namespace Resonance.Features.ImportExport
{
    public partial class ImportExportViewModel : BaseViewModel
    {
        private readonly IImportExportService _importExportService;
        private readonly ISceneDataService _sceneDataService;
        private readonly FileService _fileService;
        private readonly PackImportService _packImportService;

        private List<Campaign> _campaigns = new();
        private ExportLevel _selectedLevel = ExportLevel.StructureOnly;

        [ObservableProperty]
        private string selectedLevelLabel;

        [ObservableProperty]
        private Campaign? selectedCampaign;

        public bool RequiresCampaignSelection => _selectedLevel is ExportLevel.StructureOnly or ExportLevel.StructureWithChannels;

        public ImportExportViewModel(
            IImportExportService importExportService,
            ISceneDataService sceneDataService,
            FileService fileService,
            PackImportService packImportService)
        {
            _importExportService = importExportService;
            _sceneDataService = sceneDataService;
            _fileService = fileService;
            _packImportService = packImportService;
            selectedLevelLabel = LevelLabel(_selectedLevel);
        }

        public async Task InitializeAsync()
        {
            _campaigns = await _sceneDataService.GetCampaignsAsync();
            if (SelectedCampaign == null || _campaigns.All(c => c.Id != SelectedCampaign.Id))
                SelectedCampaign = _campaigns.FirstOrDefault();
        }

        // Remplace le bouton retour natif du Shell (cf. ImportExportPage.xaml, Shell.NavBarIsVisible="False").
        [RelayCommand]
        public async Task Back() => await Shell.Current.GoToAsync("..");

        [RelayCommand]
        private async Task SelectLevel()
        {
            var levels = new[] { ExportLevel.StructureOnly, ExportLevel.StructureWithChannels, ExportLevel.AudioLibraryOnly, ExportLevel.FullBackup };
            var labels = levels.Select(LevelLabel).ToArray();

            var index = await ShowActionSheetIndexAsync(Loc["ImportExportLevelTitle"], labels);
            if (index < 0) return;

            _selectedLevel = levels[index];
            SelectedLevelLabel = labels[index];
            OnPropertyChanged(nameof(RequiresCampaignSelection));
        }

        [RelayCommand]
        private async Task SelectCampaign()
        {
            if (_campaigns.Count == 0)
            {
                await ShowInfoAsync(Loc["ImportExportTitle"], Loc["ImportExportNoCampaigns"]);
                return;
            }

            var labels = _campaigns.Select(c => c.Title).ToArray();
            var index = await ShowActionSheetIndexAsync(Loc["ImportExportCampaignTitle"], labels);
            if (index < 0) return;

            SelectedCampaign = _campaigns[index];
        }

        [RelayCommand]
        private async Task Export()
        {
            if (RequiresCampaignSelection && SelectedCampaign == null)
            {
                await ShowInfoAsync(Loc["ImportExportTitle"], Loc["ImportExportSelectCampaignFirst"]);
                return;
            }

            var request = new ExportRequest { Level = _selectedLevel, CampaignId = SelectedCampaign?.Id ?? 0 };

            var popupView = new ImportProgressPopupView();
            popupView.ViewModel.Title = Loc["ImportExportExportInProgress"];
            popupView.ViewModel.ShowCancelButton = true;
            var page = Shell.Current.CurrentPage;
            page.ShowPopup(popupView, new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = false });

            // Basé sur le niveau choisi, pas juste sur SelectedCampaign : cette propriété peut
            // rester une valeur résiduelle d'une sélection précédente (Structure) alors que le
            // niveau actuel (Bibliothèque seule, Backup complet) n'a plus de campagne unique -
            // sans ce switch, le fichier récupérait un nom de campagne sans rapport avec son contenu.
            var baseName = _selectedLevel switch
            {
                ExportLevel.StructureOnly => $"{SelectedCampaign?.Title ?? "Campagne"}-Structure",
                ExportLevel.StructureWithChannels => $"{SelectedCampaign?.Title ?? "Campagne"}Structure-WithAudio",
                ExportLevel.AudioLibraryOnly => "Library",
                ExportLevel.FullBackup => "All",
                _ => "Export"
            };
            var fileName = SanitizeFileName($"{baseName}-{DateTime.Now:yyyyMMdd-HHmm}.dmpack");

            try
            {
                var progress = new Progress<ExportProgress>(p =>
                {
                    popupView.ViewModel.CurrentFileName = p.CurrentItem;
                    popupView.ViewModel.TotalCount = p.Total;
                    popupView.ViewModel.ProcessedCount = p.Processed;
                });

                // Construit le zip et l'enregistre à la destination choisie par l'utilisateur en une
                // seule passe, via un pipe en mémoire à taille bornée (4 Mo) plutôt qu'un fichier
                // temporaire intermédiaire : ExportAsync (producteur) et FileSaver.SaveAsync
                // (consommateur, boîte "Enregistrer sous" native) tournent en parallèle. Le pipe ne
                // retient jamais que quelques Mo à la fois - contrairement à un MemoryStream, il
                // n'atteint donc jamais la limite ~2 Go (buffer int) qui avait initialement motivé le
                // passage par un fichier temporaire, tout en évitant de réécrire les données deux fois
                // sur le disque (temp, puis destination).
                var pipe = new Pipe(new PipeOptions(
                    pauseWriterThreshold: 4 * 1024 * 1024,
                    resumeWriterThreshold: 1 * 1024 * 1024));

                using var linkedCts = new CancellationTokenSource();
                popupView.ViewModel.CancelRequested += linkedCts.Cancel;

                var writeTask = Task.Run(async () =>
                {
                    Exception? error = null;
                    try
                    {
                        var writeStream = pipe.Writer.AsStream(leaveOpen: true);
                        await using (writeStream)
                            await _importExportService.ExportAsync(request, writeStream, progress, linkedCts.Token);
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                        throw;
                    }
                    finally
                    {
                        await pipe.Writer.CompleteAsync(error);
                    }
                });

                // Limite connue et acceptée : si l'utilisateur annule en cours de route, le fichier
                // à l'emplacement choisi (boîte "Enregistrer sous") reste partiellement écrit sur le
                // disque - CommunityToolkit.Maui.Storage.FileSaver écrit directement à cet emplacement
                // (FileStream ouvert dès la sélection) et ne le supprime pas si la lecture du flux
                // source est interrompue ; pire, il catch cette exception en interne et ne renvoie
                // jamais le chemin dans ce cas (FileSaverResult(null, exception)), donc impossible de le
                // supprimer nous-mêmes après coup. Sans conséquence pratique : un .dmpack tronqué ne
                // passera jamais la vérification de signature/manifeste à l'import (cf.
                // ImportExportService.ImportAsync), il resterait juste un fichier inutile à nettoyer
                // manuellement si besoin.
                string? savedPath = null;
                Exception? saveError = null;
                try
                {
                    var readStream = pipe.Reader.AsStream(leaveOpen: true);
                    await using (readStream)
                        savedPath = await _fileService.SaveExportPackageAsync(fileName, readStream, CancellationToken.None);
                }
                catch (Exception ex)
                {
                    saveError = ex;
                }
                finally
                {
                    // Si l'enregistrement s'est arrêté (dialogue "Enregistrer sous" annulé, erreur
                    // native...) sans avoir tout lu, le producteur resterait bloqué indéfiniment à
                    // essayer d'écrire dans un pipe que plus personne ne vide : on le débloque en
                    // annulant, ce qui est sans effet s'il avait déjà terminé normalement.
                    await pipe.Reader.CompleteAsync();
                    linkedCts.Cancel();
                }

                Exception? writeError = null;
                try
                {
                    await writeTask;
                }
                catch (OperationCanceledException) when (linkedCts.IsCancellationRequested)
                {
                    // Producteur arrêté parce qu'on l'a annulé nous-mêmes ci-dessus (lecteur terminé ou
                    // en erreur), ou parce que l'utilisateur a tapé Annuler - pas une vraie erreur à
                    // remonter à l'utilisateur.
                }
                catch (Exception ex)
                {
                    writeError = ex;
                }

                // Priorité à l'erreur de construction du zip (cause racine la plus probable en cas
                // d'échec des deux côtés), sinon celle remontée par l'enregistrement. Une annulation
                // côté lecture n'est que la conséquence de PipeWriter.CompleteAsync(error) propageant
                // l'annulation du producteur au lecteur - déjà traitée comme non-erreur ci-dessus, pas
                // la peine de la remonter une seconde fois ici.
                if (writeError != null) throw writeError;
                if (saveError is not null and not OperationCanceledException) throw saveError;

                if (savedPath != null)
                    await ShowInfoAsync(Loc["ImportExportTitle"], string.Format(Loc["ImportExportExportSuccess"], savedPath));
            }
            catch (Exception ex)
            {
                await ShowErrorAsync(ex);
            }
            finally
            {
                await page.ClosePopupAsync();
            }
        }

        [RelayCommand]
        private async Task Import()
        {
            var popupView = new ImportProgressPopupView();
            popupView.ViewModel.Title = Loc["ImportExportImportInProgress"];
            popupView.ViewModel.ShowCancelButton = true;
            var page = Shell.Current.CurrentPage;
            page.ShowPopup(popupView, new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = false });

            using var cts = new CancellationTokenSource();
            popupView.ViewModel.CancelRequested += cts.Cancel;

            FileResult? file = null;
            try
            {
                // Popup affichée dès maintenant (avant même la sélection) : sur Android, un fichier
                // choisi depuis un fournisseur de contenu (Téléchargements, Drive...) est d'abord
                // recopié dans le cache de l'appli avant de pouvoir être lu (cf.
                // FileService.PickImportPackageAndroidAsync) - potentiellement long pour un gros pack,
                // d'où ce retour de progression sur la copie plutôt que de laisser l'appli paraître
                // figée pendant que ça travaille en coulisses.
                var copyProgress = new Progress<long>(bytesRead =>
                    popupView.ViewModel.CurrentFileName = string.Format(Loc["ImportExportCopyingFile"], bytesRead / (1024.0 * 1024.0)));

                file = await _fileService.PickImportPackageAsync(Loc["ImportExportPickFile"], copyProgress, cts.Token);
                if (file == null)
                {
                    await page.ClosePopupAsync();
                    return;
                }

                popupView.ViewModel.CurrentFileName = string.Empty;

                using var stream = await file.OpenReadAsync();
                var (result, coverDuration) = await _packImportService.ImportAsync(stream, popupView.ViewModel, cts.Token);

                await page.ClosePopupAsync();
                await InitializeAsync();

                var summary = string.Format(
                    Loc["ImportExportImportSummary"],
                    result.CampaignsImported, result.TracksCopied, result.TracksReused, result.TracksRejected, result.SpellsImported);

                // Le détail des causes de rejet n'a d'intérêt que s'il y a effectivement des rejets -
                // évite d'alourdir le message pour le cas normal (aucune piste rejetée).
                if (result.TracksRejected > 0)
                    summary += "\n" + string.Format(Loc["ImportExportRejectDetails"],
                        result.TracksRejectedHashMismatch, result.TracksRejectedNotDecodable,
                        result.TracksRejectedMissingEntry, result.TracksRejectedOther);

                // Durée affichée en toutes circonstances (pas juste en cas de lenteur perçue) : donne à
                // l'utilisateur un repère concret, et à nous un chiffre exploitable s'il nous remonte
                // un ressenti de lenteur plutôt qu'une simple impression. Le total inclut la préchauffe
                // des pochettes (result.TotalDuration ne la couvre pas, calculée côté Core avant elle).
                summary += "\n" + string.Format(Loc["ImportExportDurationSummary"],
                    FormatDuration(result.TotalDuration + coverDuration), FormatDuration(result.ExtractionDuration),
                    FormatDuration(result.VerificationDuration), FormatDuration(coverDuration));

                await ShowInfoAsync(Loc["ImportExportTitle"], summary);
            }
            catch (OperationCanceledException)
            {
                // Pas de rollback : une campagne/piste déjà insérée avant l'annulation reste en base
                // (comportement déjà documenté pour toute interruption d'import, cf. ImportCampaignAsync
                // côté service) - on rafraîchit donc les listes plutôt que de les laisser figées, mais
                // sans le résumé final puisque ImportAsync n'a pas eu l'occasion de le retourner.
                PackImportService.NotifyListsChanged();
                await page.ClosePopupAsync();
                await InitializeAsync();
            }
            catch (Exception ex)
            {
                await page.ClosePopupAsync();
                await ShowErrorAsync(ex);
            }
            finally
            {
                // Sur Android, le fichier choisi est une copie locale dans le cache de l'appli (cf.
                // PickImportPackageAndroidAsync), pas l'original de l'utilisateur - sans ce nettoyage
                // immédiat, elle resterait à occuper de l'espace jusqu'au prochain démarrage de l'appli
                // (ClearPickerCache). DeleteIfCached ne touche jamais un fichier hors du cache
                // (Windows/MacCatalyst renvoient le chemin réel de l'utilisateur, jamais une copie -
                // rien à supprimer dans ce cas).
                if (file != null)
                    _fileService.DeleteIfCached(file.FullPath);

                await Back();
            }
        }

        private static string LevelLabel(ExportLevel level) => level switch
        {
            ExportLevel.StructureOnly => LocalizationService.Instance["ImportExportLevel1"],
            ExportLevel.StructureWithChannels => LocalizationService.Instance["ImportExportLevel2"],
            ExportLevel.AudioLibraryOnly => LocalizationService.Instance["ImportExportLevel3"],
            ExportLevel.FullBackup => LocalizationService.Instance["ImportExportLevel4"],
            _ => level.ToString()
        };

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return name;
        }

        private static string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalSeconds < 60)
                return $"{duration.TotalSeconds:F0} s";
            if (duration.TotalMinutes < 60)
                return $"{(int)duration.TotalMinutes} min {duration.Seconds:D2} s";
            return $"{(int)duration.TotalHours} h {duration.Minutes:D2} min";
        }
    }
}
