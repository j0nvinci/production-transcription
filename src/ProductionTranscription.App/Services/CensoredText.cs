using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace ProductionTranscription.App.Services;

/// <summary>Renders masked terms as individually hoverable spans without showing them by default.</summary>
public static class CensoredText
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(CensoredText), new PropertyMetadata("", Changed));
    public static readonly DependencyProperty FilterProperty = DependencyProperty.RegisterAttached(
        "Filter", typeof(TranscriptCensor), typeof(CensoredText), new PropertyMetadata(null, Changed));

    public static string GetText(DependencyObject target) => (string)target.GetValue(TextProperty);
    public static void SetText(DependencyObject target, string text) => target.SetValue(TextProperty, text);
    public static TranscriptCensor? GetFilter(DependencyObject target) =>
        (TranscriptCensor?)target.GetValue(FilterProperty);
    public static void SetFilter(DependencyObject target, TranscriptCensor filter) => target.SetValue(FilterProperty, filter);

    private static void Changed(DependencyObject target, DependencyPropertyChangedEventArgs _)
    {
        if (target is not TextBlock block) return;
        string text = GetText(block) ?? "";
        var filter = GetFilter(block);
        block.Inlines.Clear();
        if (filter is null) { block.Inlines.Add(new Run(text)); return; }

        int pos = 0;
        foreach (var hit in filter.FindMatches(text))
        {
            if (hit.Start < pos || hit.Start + hit.Length > text.Length) continue;
            if (hit.Start > pos) block.Inlines.Add(new Run(text.Substring(pos, hit.Start - pos)));
            var span = new Span(new Run(new string('•', hit.Length)));
            span.Cursor = Cursors.Help;
            span.TextDecorations = TextDecorations.Underline;
            ToolTipService.SetToolTip(span, hit.Original);
            ToolTipService.SetInitialShowDelay(span, 250);
            block.Inlines.Add(span);
            pos = hit.Start + hit.Length;
        }
        if (pos < text.Length) block.Inlines.Add(new Run(text.Substring(pos)));
    }
}
