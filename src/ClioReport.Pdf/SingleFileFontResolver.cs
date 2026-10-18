using PdfSharp.Fonts;

namespace ClioReport.Pdf;

/// <summary>
/// PDFsharp Core loads no fonts by itself; this resolves requests to up to four TrueType files. A style without its
/// own file is drawn with the regular file and simulated by PDFsharp. An optional fallback font (family
/// <see cref="FallbackFamily"/>) is used by the renderer for characters the main font lacks.
/// </summary>
internal sealed class SingleFileFontResolver : IFontResolver
{
    public const string FallbackFamily = "ClioReport Fallback";

    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _data = new(StringComparer.Ordinal);

    public SingleFileFontResolver(
        string regular, string? bold = null, string? italic = null, string? boldItalic = null,
        string? fallback = null, string? fallbackBold = null)
    {
        PrimaryPath = regular;
        FallbackPath = fallback;
        _paths["ClioReport#Regular"] = regular;
        if (bold is not null) _paths["ClioReport#Bold"] = bold;
        if (italic is not null) _paths["ClioReport#Italic"] = italic;
        if (boldItalic is not null) _paths["ClioReport#BoldItalic"] = boldItalic;
        if (fallback is not null) _paths["ClioReport.Fallback#Regular"] = fallback;
        if (fallbackBold is not null) _paths["ClioReport.Fallback#Bold"] = fallbackBold;
    }

    public string PrimaryPath { get; }

    public string? FallbackPath { get; }

    public bool HasFallback => FallbackPath is not null;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        if (HasFallback && familyName == FallbackFamily)
        {
            var face = bold && _paths.ContainsKey("ClioReport.Fallback#Bold") ? "ClioReport.Fallback#Bold" : "ClioReport.Fallback#Regular";
            var simulateBold = bold && face.EndsWith("Regular", StringComparison.Ordinal);
            return new FontResolverInfo(face, simulateBold, italic);
        }

        var name = (bold, italic) switch
        {
            (true, true) => "ClioReport#BoldItalic",
            (true, false) => "ClioReport#Bold",
            (false, true) => "ClioReport#Italic",
            _ => "ClioReport#Regular"
        };
        return _paths.ContainsKey(name)
            ? new FontResolverInfo(name, false, false)
            : new FontResolverInfo("ClioReport#Regular", bold, italic);
    }

    public byte[]? GetFont(string faceName)
    {
        lock (_data)
        {
            if (!_data.TryGetValue(faceName, out var bytes))
                _data[faceName] = bytes = File.ReadAllBytes(_paths[faceName]);
            return bytes;
        }
    }
}
