using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProductionTranscription.App.Models;
namespace ProductionTranscription.App.Services;

public static class SettingsStore
{
    private static readonly object SaveLock = new();
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ProductionTranscription", "settings.json");
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Options)
                    ?? new AppSettings();
        }
        catch (Exception) { /* Invalid settings must not prevent application startup. */ }
        return new AppSettings();
    }
    public static void Save(AppSettings settings)
    {
        lock (SaveLock)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            string temporary = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
                if (File.Exists(FilePath))
                    File.Replace(temporary, FilePath, null);
                else
                    File.Move(temporary, FilePath);
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
