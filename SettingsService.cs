using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NetworkTracker;

internal static class SettingsService
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "NetworkTracker",
        "settings.json");

    public static AppSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
                return new AppSettings();

            string json = File.ReadAllText(FilePath);
            AppSettings? settings = JsonSerializer.Deserialize<AppSettings>(json, Options);
            if (settings == null)
                return new AppSettings();

            if (double.IsNaN(settings.Width) || settings.Width < 220 || settings.Width > 720)
                settings.Width = 300;

            return settings;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            AppLog.Write(ex);
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        lock (Gate)
        {
            try
            {
                string? directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string json = JsonSerializer.Serialize(settings, Options);
                string temp = FilePath + ".tmp";
                File.WriteAllText(temp, json);
                File.Move(temp, FilePath, overwrite: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLog.Write(ex);
            }
        }
    }
}
