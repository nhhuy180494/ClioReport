using PdfSharp.Drawing;
using PdfSharp.Fonts;

namespace ClioReport.Pdf;

/// <summary>Creates and reuses one XFont per style/size, and splits text between the main and the fallback font.</summary>
internal sealed class FontCache
{
    private readonly PdfRenderOptions _options;
    private readonly Dictionary<TextStyle, XFont> _fonts = new();
    private readonly Dictionary<TextStyle, XFont> _fallbackFonts = new();
    private readonly TrueTypeCoverage? _primary;
    private readonly TrueTypeCoverage? _fallback;
    private readonly Dictionary<string, TrueTypeCoverage?> _familyCoverage = new(StringComparer.Ordinal);

    public FontCache(PdfRenderOptions options)
    {
        _options = options;
        if (GlobalFontSettings.FontResolver is SingleFileFontResolver { HasFallback: true } resolver)
        {
            _primary = resolver.Coverage(null);
            _fallback = resolver.Coverage(null, fallback: true);
        }
    }

    public XFont Get(TextStyle style) => Create(style, FamilyOf(style) ?? _options.FontFamily, _fonts);

    private static SingleFileFontResolver? Resolver => GlobalFontSettings.FontResolver as SingleFileFontResolver;

    /// <summary>The registered family key an element asked for, or null for the default font.</summary>
    private static string? FamilyOf(TextStyle style) => Resolver?.FamilyKey(style.FontFamily);

    private TrueTypeCoverage? PrimaryFor(TextStyle style)
    {
        var key = FamilyOf(style);
        if (key is null) return _primary;
        if (!_familyCoverage.TryGetValue(key, out var cov))
        {
            _familyCoverage[key] = cov = Resolver!.Coverage(key);
        }
        return cov;
    }

    /// <summary>Runs of text with the font to draw each with. Without a usable fallback it is one run in the main font.</summary>
    public List<(string Text, XFont Font)> Runs(string text, TextStyle style)
    {
        var primary = PrimaryFor(style);
        Func<int, bool>? primaryHas = primary is null ? null : new Func<int, bool>(primary.Has);
        Func<int, bool>? fallbackHas = _fallback is null ? null : new Func<int, bool>(_fallback.Has);
        var runs = TextRuns.Split(text, primaryHas, fallbackHas);
        var result = new List<(string, XFont)>(runs.Count);
        foreach (var run in runs)
        {
            var font = run.Fallback ? Create(style, SingleFileFontResolver.FallbackFamily, _fallbackFonts) : Get(style);
            result.Add((run.Text, font));
        }
        return result;
    }

    private XFont Create(TextStyle style, string family, Dictionary<TextStyle, XFont> cache)
    {
        if (cache.TryGetValue(style, out var font)) return font;

        var size = style.FontSize > 0 ? style.FontSize : _options.FontSize;
        var kind = (style.Bold, style.Italic) switch
        {
            (true, true) => XFontStyleEx.BoldItalic,
            (true, false) => XFontStyleEx.Bold,
            (false, true) => XFontStyleEx.Italic,
            _ => XFontStyleEx.Regular
        };
        return cache[style] = new XFont(family, size, kind);
    }
}
