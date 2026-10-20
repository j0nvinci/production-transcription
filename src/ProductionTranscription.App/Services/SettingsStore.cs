using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProductionTranscription.App.Models;
namespace ProductionTranscription.App.Services;

public static class SettingsStore
{
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
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        var temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, Options));
        File.Move(temporary, FilePath, true);
    }
}
