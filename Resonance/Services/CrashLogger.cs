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
}
