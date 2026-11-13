using System.IO;

namespace NetworkTracker;

internal static class AppLog
{
    public static void Write(Exception exception)
    {
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "NetworkTracker");
            Directory.CreateDirectory(directory);
            string path = Path.Combine(directory, "error.log");
            File.AppendAllText(path, $"--- {DateTimeOffset.Now:u}{Environment.NewLine}{exception}{Environment.NewLine}");
        }
        catch
        {
            // Logging must not take down the tray icon.
        }
    }
}
