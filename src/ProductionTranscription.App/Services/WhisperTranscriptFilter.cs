using System.Text.RegularExpressions;

namespace ProductionTranscription.App.Services;

/// <summary>
/// Removes Whisper's standalone non-speech captions while preserving spoken text.
/// Only explicitly bracketed descriptions are removed; ordinary speech is untouched.
/// </summary>
public static class WhisperTranscriptFilter
{
    private static readonly Regex Caption = new(
        @"(?ix)
        [\(\[]
        \s*
        (?:
            (?:gentle|soft|loud|upbeat|dramatic|background|instrumental|faint|distant|ominous|suspenseful|calm|quiet|intense|fast|slow|sad|happy|tense|muffled|continuous|repeated|rapid|brief|indistinct|mechanical|electronic)\s+
        )*
        (?:music|applause|clapping|cheering|laughter|laughing|chuckling|giggling|sobbing|crying|screaming|sighing|gasping|coughing|breathing|snoring|whistling|humming|singing|silence|static|noise|buzzing|beeping|ringing|whirring|machine\s+whirring|engine\s+(?:running|revving)|footsteps|door\s+(?:opening|closing)|birds?\s+chirping|wind\s+blowing|rain\s+falling)
        (?:\s+(?:playing|continues|fades|fading|stops|in\s+background|sound|sounds|noises))?
        \s*
        [\)\]]",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex Whitespace = new(@"\s{2,}", RegexOptions.Compiled);

    public static string Clean(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return "";
        try
        {
            return Whitespace.Replace(Caption.Replace(text, ""), " ").Trim();
        }
        catch (RegexMatchTimeoutException)
        {
            return text.Trim();
        }
    }
}
