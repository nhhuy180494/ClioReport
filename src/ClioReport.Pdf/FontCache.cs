using PdfSharp.Drawing;

namespace ClioReport.Pdf;

/// <summary>Creates and reuses one XFont per style/size.</summary>
internal sealed class FontCache
{
    private readonly PdfRenderOptions _options;
    private readonly Dictionary<TextStyle, XFont> _fonts = new();

    public FontCache(PdfRenderOptions options) => _options = options;

    public XFont Get(TextStyle style)
    {
        if (_fonts.TryGetValue(style, out var font)) return font;

        var size = style.FontSize > 0 ? style.FontSize : _options.FontSize;
        var kind = (style.Bold, style.Italic) switch
        {
            (true, true) => XFontStyleEx.BoldItalic,
            (true, false) => XFontStyleEx.Bold,
            (false, true) => XFontStyleEx.Italic,
            _ => XFontStyleEx.Regular
        };
        return _fonts[style] = new XFont(_options.FontFamily, size, kind);
    }
}
