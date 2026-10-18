using PdfSharp.Fonts;

namespace ClioReport.Pdf;

/// <summary>PDFsharp Core loads no fonts by itself; this resolves every request to one TrueType file.</summary>
internal sealed class SingleFileFontResolver : IFontResolver
{
    private const string FaceName = "ClioReport#Regular";

    private readonly string _path;
    private byte[]? _data;

    public SingleFileFontResolver(string path) => _path = path;

    public FontResolverInfo? ResolveTypeface(string familyName, bool bold, bool italic) =>
        new(FaceName, false, false);

    public byte[]? GetFont(string faceName) => _data ??= File.ReadAllBytes(_path);
}
