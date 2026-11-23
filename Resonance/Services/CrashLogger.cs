namespace Resonance.Services;

/// <summary>Journal fichier minimal actif en Debug ET Release (contrairement à builder.Logging.AddDebug(),
/// DEBUG seulement, invisible une fois l'appli lancée hors Visual Studio). Écrit ligne par ligne, sans
/// tampon (un vrai plantage ne laisse pas le temps de vider un buffer), dans
/// FileSystem.AppDataDirectory/crash.log - consultable après coup sans débogueur attaché.</summary>
public static class CrashLogger
{
    // Au-delà, le journal est repris de zéro : il ne sert qu'à diagnostiquer les derniers incidents.
    private const long MaxLogBytes = 512 * 1024;

    private static readonly string LogPath = Path.Combine(FileSystem.AppDataDirectory, "crash.log");
    private static readonly object Lock = new();

    public static void Log(string message)
    {
        System.Diagnostics.Debug.WriteLine($"[CrashLogger] {message}");
        try
        {
            lock (Lock)
            {
                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > MaxLogBytes)
                    info.Delete();
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{Environment.CurrentManagedThreadId}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // Le journal ne doit jamais devenir lui-même une source de plantage.
        }
    }

    public static void LogException(string context, Exception ex) =>
        Log($"EXCEPTION in {context}: {ex}");

    /// <summary>Vrai si le journal contient au moins une entrée (cf. Paramètres › Signaler un bug).</summary>
    public static bool HasEntries
    {
        get
        {
            try { return new FileInfo(LogPath).Length > 0; }
            catch { return false; }
        }
    }

    /// <summary>
    /// Copie du journal à partager, sous un nom lisible dans le cache de l'appli : le partage ne
    /// garde pas le fichier ouvert pendant que l'appli continue d'y écrire.
    /// </summary>
    public static string CreateShareableCopy()
    {
        var copy = Path.Combine(FileSystem.CacheDirectory, $"resonance-journal-{DateTime.Now:yyyyMMdd-HHmm}.txt");
        lock (Lock)
            File.Copy(LogPath, copy, overwrite: true);
        return copy;
    }
}
