namespace Resonance.Services
{
    /// <summary>
    /// Abstraction du stockage physique des fichiers audio locaux (implémentée côté MAUI par
    /// FileService), pour que les services Core restent testables sans dépendance MAUI.
    /// </summary>
    public interface ITrackFileStore
    {
        string CopyTrackToLocal(string originalFilePath);

        /// <summary>Réserve un chemin définitif (dossier Tracks) pour y écrire directement un fichier
        /// à venir, sans passer par une copie ultérieure - cf. ImportExportService.ImportTrackAsync.</summary>
        string ReserveTrackPath(string extension);
    }
}
