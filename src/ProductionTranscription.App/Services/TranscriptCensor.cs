using System.IO;
using System.Text.RegularExpressions;

namespace ProductionTranscription.App.Services;

/// <summary>
/// Display-only word/phrase masking. This is not a comprehensive content-moderation engine.
/// The user-editable term list is stored in LocalApplicationData and read at app startup.
/// </summary>
public sealed class TranscriptCensor
{
    public string TermsPath { get; }
    private readonly Regex? _pattern;

    private static readonly string[] Defaults =
    {
        "fuck", "fucking", "fucked", "fucker", "motherfucker", "motherfucking",
        "shit", "shitty", "bullshit", "bitch", "bitches", "asshole", "assholes",
        "dick", "cock", "pussy", "cunt", "bastard", "damn", "goddamn",
        "nigger", "nigga", "faggot", "fag", "retard", "retarded",
        "spic", "chink", "kike", "wetback", "dyke"
    };

    public TranscriptCensor()
    {
        string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProductionTranscription");
        Directory.CreateDirectory(folder);
        TermsPath = Path.Combine(folder, "censored-terms.txt");
        if (!File.Exists(TermsPath))
        {
            // Only words on their own lines; operators can modify the list for their venue.
            File.WriteAllLines(TermsPath,
                new[] { "# One word or phrase per line. Lines starting with # are ignored." }.Concat(Defaults));
        }
        var terms = File.ReadAllLines(TermsPath)
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 && !t.StartsWith('#'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => t.Length)
            .Select(t => Regex.Escape(t).Replace(@"\ ", @"\s+"))
            .ToArray();
        if (terms.Length > 0)
            _pattern = new Regex(@"(?<![\p{L}\p{N}])(?:" + string.Join("|", terms) +
                @")(?![\p{L}\p{N}])", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                TimeSpan.FromMilliseconds(250));
    }

    public string Censor(string text)
    {
        if (_pattern is null || string.IsNullOrEmpty(text)) return text;
        try
        {
            return _pattern.Replace(text, match => new string('•', match.Length));
        }
        catch (RegexMatchTimeoutException) { return "[text filtered]"; }
    }
}
