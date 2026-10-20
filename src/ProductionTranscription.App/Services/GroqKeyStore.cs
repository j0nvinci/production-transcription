using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace ProductionTranscription.App.Services;
public static class GroqKeyStore
{
    private static string Location => Path.Combine(Environment.GetFolderPath(
        Environment.SpecialFolder.LocalApplicationData), "ProductionTranscription", "groq-key.dat");
    public static string Load()
    {
        if (!File.Exists(Location)) return "";
        return Encoding.UTF8.GetString(ProtectedData.Unprotect(
            File.ReadAllBytes(Location), null, DataProtectionScope.CurrentUser));
    }
    public static void Save(string secret)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Location)!);
        if (string.IsNullOrWhiteSpace(secret))
        {
            if (File.Exists(Location)) File.Delete(Location);
            return;
        }
        File.WriteAllBytes(Location, ProtectedData.Protect(
            Encoding.UTF8.GetBytes(secret.Trim()), null, DataProtectionScope.CurrentUser));
    }
}
