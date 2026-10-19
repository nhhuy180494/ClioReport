using PdfSharp.Fonts;

namespace ClioReport.Pdf;

/// <summary>
/// PDFsharp Core loads no fonts by itself; this resolves requests to TrueType files or in-memory fonts. A style without
/// its own font is drawn with the regular one and simulated by PDFsharp. Extra families are looked up by the name an
/// element asks for. An optional fallback font (family <see cref="FallbackFamily"/>) is used by the renderer for
/// characters the main font lacks.
/// </summary>
internal sealed class SingleFileFontResolver : IFontResolver
{
    public const string FallbackFamily = "ClioReport Fallback";

    private readonly Dictionary<string, FontSource> _sources = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _data = new(StringComparer.Ordinal);
    private readonly HashSet<string> _families = new(StringComparer.Ordinal);

    private static string Normalize(string name) => "Family:" + name.Trim().ToLowerInvariant();

    public SingleFileFontResolver(
        FontSource regular, FontSource? bold = null, FontSource? italic = null, FontSource? boldItalic = null,
        FontSource? fallback = null, FontSource? fallbackBold = null, IReadOnlyDictionary<string, PdfFontFiles>? families = null)
    {
        if (families is not null)
            foreach (var (name, files) in families)
            {
                var key = Normalize(name);
                _sources[key + "#Regular"] = files.Regular;
                if (files.Bold is not null) _sources[key + "#Bold"] = files.Bold;
                if (files.Italic is not null) _sources[key + "#Italic"] = files.Italic;
                if (files.BoldItalic is not null) _sources[key + "#BoldItalic"] = files.BoldItalic;
                _families.Add(key);
            }
        _sources["ClioReport#Regular"] = regular;
        if (bold is not null) _sources["ClioReport#Bold"] = bold;
        if (italic is not null) _sources["ClioReport#Italic"] = italic;
        if (boldItalic is not null) _sources["ClioReport#BoldItalic"] = boldItalic;
        if (fallback is not null) _sources["ClioReport.Fallback#Regular"] = fallback;
        if (fallbackBold is not null) _sources["ClioReport.Fallback#Bold"] = fallbackBold;
    }

    public bool HasFallback => _sources.ContainsKey("ClioReport.Fallback#Regular");

    /// <summary>The key to ask PDFsharp for, or null when <paramref name="name"/> is not a registered family.</summary>
    public string? FamilyKey(string? name) =>
        !string.IsNullOrWhiteSpace(name) && _families.Contains(Normalize(name)) ? Normalize(name) : null;

    /// <summary>Glyph coverage of the regular face of the main font (<c>null</c> key) or of a registered family.</summary>
    public TrueTypeCoverage? Coverage(string? familyKey, bool fallback = false)
    {
        var face = fallback ? "ClioReport.Fallback#Regular" : (familyKey ?? "ClioReport") + "#Regular";
        try { return TrueTypeCoverage.Parse(GetFont(face)!); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or KeyNotFoundException) { return null; }
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (HasFallback && familyName == FallbackFamily)
        {
            var face = bold && _sources.ContainsKey("ClioReport.Fallback#Bold") ? "ClioReport.Fallback#Bold" : "ClioReport.Fallback#Regular";
            var simulateBold = bold && face.EndsWith("Regular", StringComparison.Ordinal);
            return new FontResolverInfo(face, simulateBold, italic);
        }

        if (_families.Contains(familyName))
        {
            var style = (bold, italic) switch { (true, true) => "BoldItalic", (true, false) => "Bold", (false, true) => "Italic", _ => "Regular" };
            return _sources.ContainsKey(familyName + "#" + style)
                ? new FontResolverInfo(familyName + "#" + style, false, false)
                : new FontResolverInfo(familyName + "#Regular", bold, italic);
        }

        var name = (bold, italic) switch
        {
            (true, true) => "ClioReport#BoldItalic",
            (true, false) => "ClioReport#Bold",
            (false, true) => "ClioReport#Italic",
            _ => "ClioReport#Regular"
        };
        return _sources.ContainsKey(name)
            ? new FontResolverInfo(name, false, false)
            : new FontResolverInfo("ClioReport#Regular", bold, italic);
    }

    public byte[]? GetFont(string faceName)
    {
        lock (_data)
        {
            if (!_data.TryGetValue(faceName, out var bytes))
                _data[faceName] = bytes = _sources[faceName].Read();
            return bytes;
        }
    }
}
