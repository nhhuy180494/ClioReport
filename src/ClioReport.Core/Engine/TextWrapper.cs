namespace ClioReport;

internal static class TextWrapper
{
    /// <summary>
    /// Greedy word wrap. '\n' starts a new paragraph (an empty paragraph keeps a blank line);
    /// a word wider than the box is broken by characters.
    /// </summary>
    public static List<string> Wrap(string text, double maxWidth, ITextMeasurer measurer, TextStyle style = default)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (measurer.MeasureWidth(candidate, style) <= maxWidth)
                {
                    line = candidate;
                    continue;
                }

                if (line.Length > 0)
                {
                    lines.Add(line);
                    line = "";
                }

                var rest = word;
                while (rest.Length > 1 && measurer.MeasureWidth(rest, style) > maxWidth)
                {
                    var n = FitCount(rest, maxWidth, measurer, style);
                    lines.Add(rest[..n]);
                    rest = rest[n..];
                }
                line = rest;
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>Number of leading characters that fit in maxWidth (at least 1).</summary>
    private static int FitCount(string s, double maxWidth, ITextMeasurer measurer, TextStyle style)
    {
        var n = 1;
        while (n < s.Length && measurer.MeasureWidth(s[..(n + 1)], style) <= maxWidth) n++;
        return n;
    }
}
