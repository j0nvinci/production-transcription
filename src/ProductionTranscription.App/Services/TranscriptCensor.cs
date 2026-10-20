using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;

namespace ProductionTranscription.App.Services;

/// <summary>
/// Display-only word/phrase masking. This is not a comprehensive content-moderation engine.
/// The user-editable term list is stored in LocalApplicationData and read at app startup.
/// </summary>
public sealed class TranscriptCensor
{
    public string TermsPath { get; }
    private Regex? _pattern;
    private readonly string _cachePath;
    private readonly string _stampPath;
    private static readonly HttpClient Client = new() { Timeout = TimeSpan.FromSeconds(12) };
    private const string ListUrl = "https://raw.githubusercontent.com/censor-text/profanity-list/main/list/en.txt";
    public string? LastUpdateError { get; private set; }

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
        _cachePath = Path.Combine(folder, "community-profanity-en.txt");
        _stampPath = Path.Combine(folder, "community-profanity-last-update.txt");
        TermsPath = Path.Combine(folder, "censored-terms.txt");
        if (!File.Exists(TermsPath))
        {
            // Only words on their own lines; operators can modify the list for their venue.
            File.WriteAllLines(TermsPath,
                new[] { "# One word or phrase per line. Lines starting with # are ignored." }.Concat(Defaults));
        }
        RebuildPattern();
    }

    private void RebuildPattern()
    {
        var terms = File.ReadLines(TermsPath)
            .Concat(File.Exists(_cachePath) ? File.ReadLines(_cachePath) : Enumerable.Empty<string>())
            .Select(t => t.Trim())
            .Where(t => t.Length > 0 && !t.StartsWith('#') && t.Length <= 120)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(t => t.Length)
            .Select(t => Regex.Escape(t).Replace(@"\ ", @"\s+"))
            .ToArray();
        Regex? updated = terms.Length == 0 ? null : new Regex(
            @"(?<![\p{L}\p{N}])(?:" + string.Join("|", terms) + @")(?![\p{L}\p{N}])",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        Volatile.Write(ref _pattern, updated);
    }

    public async Task UpdateCommunityListAsync()
    {
        try
        {
            if (File.Exists(_cachePath) && File.Exists(_stampPath)
                && DateTime.TryParse(File.ReadAllText(_stampPath), out var last)
                && DateTime.UtcNow - last.ToUniversalTime() < TimeSpan.FromDays(1))
                return;
            string downloaded = await Client.GetStringAsync(ListUrl).ConfigureAwait(false);
            var lines = downloaded.Replace("\r", "").Split('\n')
                .Select(x => x.Trim()).Where(x => x.Length > 0 && x.Length <= 120).ToArray();
            if (lines.Length < 100)
                throw new InvalidDataException("Community list download was unexpectedly short.");
            string temporary = _cachePath + ".tmp";
            await File.WriteAllLinesAsync(temporary, lines).ConfigureAwait(false);
            File.Move(temporary, _cachePath, true);
            await File.WriteAllTextAsync(_stampPath, DateTime.UtcNow.ToString("O")).ConfigureAwait(false);
            RebuildPattern();
            LastUpdateError = null;
        }
        catch (Exception ex)
        {
            LastUpdateError = ex.Message;
        }
    }

        public IEnumerable<(int Start, int Length, string Original)> FindMatches(string text)
    {
        var pattern = Volatile.Read(ref _pattern);
        if (pattern is null || string.IsNullOrEmpty(text)) yield break;
        MatchCollection matches;
        try { matches = pattern.Matches(text); }
        catch (RegexMatchTimeoutException) { yield break; }
        // Enumerate defensively: a malformed or slow third-party list must not break the UI.
        var safe = new List<(int, int, string)>();
        try
        {
            foreach (Match m in matches)
                if (m.Success && m.Length > 0) safe.Add((m.Index, m.Length, m.Value));
        }
        catch (RegexMatchTimeoutException) { yield break; }
        foreach (var m in safe) yield return m;
    }

        public string Censor(string text)
    {
        var pattern = Volatile.Read(ref _pattern);
        if (pattern is null || string.IsNullOrEmpty(text)) return text;
        try
        {
            return pattern.Replace(text, match => new string('•', match.Length));
        }
        catch (RegexMatchTimeoutException) { return "[text filtered]"; }
    }
}
