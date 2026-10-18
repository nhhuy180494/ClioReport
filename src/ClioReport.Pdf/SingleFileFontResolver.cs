using PdfSharp.Fonts;

namespace ClioReport.Pdf;

/// <summary>
/// PDFsharp Core loads no fonts by itself; this resolves requests to up to four TrueType files. A style without its
/// own file is drawn with the regular file and simulated by PDFsharp.
/// </summary>
internal sealed class SingleFileFontResolver : IFontResolver
{
    private readonly Dictionary<string, string> _paths = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _data = new(StringComparer.Ordinal);

    public SingleFileFontResolver(string regular, string? bold = null, string? italic = null, string? boldItalic = null)
    {
        _paths["ClioReport#Regular"] = regular;
        if (bold is not null) _paths["ClioReport#Bold"] = bold;
        if (italic is not null) _paths["ClioReport#Italic"] = italic;
        if (boldItalic is not null) _paths["ClioReport#BoldItalic"] = boldItalic;
    }

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic)
    {
        var face = (bold, italic) switch
        {
            (true, true) => "ClioReport#BoldItalic",
            (true, false) => "ClioReport#Bold",
            (false, true) => "ClioReport#Italic",
            _ => "ClioReport#Regular"
        };
        return _paths.ContainsKey(face)
            ? new FontResolverInfo(face, false, false)
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
