using System.Globalization;
using System.IO;

namespace Logbook.App.Diagnostics;

/// <summary>
/// Rolling text log in %LOCALAPPDATA%\Logbook\logs, one file per day, two weeks kept (docs/development.md).
/// Never write an entry's contents here.
/// </summary>
public static class Diag
{
    private static readonly object Gate = new();
    private static readonly string Folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Logbook", "logs");

    public static string LogFolder => Folder;

    public static void Log(string message)
    {
        string line = string.Create(CultureInfo.InvariantCulture, $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}");
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(TodayPath(), line + Environment.NewLine);
            }
            catch (IOException)
            {
                // Diagnostics are best effort; nothing depends on them.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    public static void Log(string message, Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        Log($"{message}: {exception.GetType().Name}: {exception.Message}{Environment.NewLine}{exception.StackTrace}");
    }

    /// <summary>Deletes log files older than two weeks. Called once at startup.</summary>
    public static void Prune()
    {
        try
        {
            if (!Directory.Exists(Folder))
            {
                return;
            }

            DateTime cutoff = DateTime.Now.AddDays(-14);
            foreach (string file in Directory.GetFiles(Folder, "logbook-*.log"))
            {
                if (File.GetLastWriteTime(file) < cutoff)
                {
                    File.Delete(file);
                }
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string TodayPath() => Path.Combine(Folder, DateTime.Now.ToString("'logbook-'yyyy-MM-dd'.log'", CultureInfo.InvariantCulture));
}
