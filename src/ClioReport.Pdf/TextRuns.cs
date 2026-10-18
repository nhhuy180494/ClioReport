using System.Text;

namespace ClioReport.Pdf;

internal static class TextRuns
{
    /// <summary>
    /// Splits text into runs drawn with the primary or the fallback font. A character goes to the fallback only when
    /// the primary font lacks it and the fallback has it; spaces stay with the run before them.
    /// </summary>
    public static List<(string Text, bool Fallback)> Split(string text, Func<int, bool>? primaryHas, Func<int, bool>? fallbackHas)
    {
        var runs = new List<(string Text, bool Fallback)>();
        if (text.Length == 0) return runs;
        if (primaryHas is null || fallbackHas is null)
        {
            runs.Add((text, false));
            return runs;
        }

        var current = new StringBuilder();
        var currentFallback = false;
        foreach (var rune in text.EnumerateRunes())
        {
            var useFallback = currentFallback;
            if (!Rune.IsWhiteSpace(rune))
                useFallback = !primaryHas(rune.Value) && fallbackHas(rune.Value);

            if (current.Length > 0 && useFallback != currentFallback)
            {
                runs.Add((current.ToString(), currentFallback));
                current.Clear();
            }
            currentFallback = useFallback;
            current.Append(rune.ToString());
        }
        runs.Add((current.ToString(), currentFallback));
        return runs;
    }
}
