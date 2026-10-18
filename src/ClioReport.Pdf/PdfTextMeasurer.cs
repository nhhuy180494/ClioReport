using PdfSharp.Drawing;

namespace ClioReport.Pdf;

/// <summary>
/// Measures text with the same font <see cref="PdfReportRenderer"/> draws with. Pass it to
/// <c>ReportEngine.Render</c> to enable <c>wrap</c>; use the same <see cref="PdfRenderOptions"/> for both.
/// </summary>
public sealed class PdfTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly XGraphics _gfx;
    private readonly FontCache _fonts;

    public PdfTextMeasurer(PdfRenderOptions? options = null)
    {
        options ??= new PdfRenderOptions();
        PdfReportRenderer.EnsureFontResolver(options);

        _fonts = new FontCache(options);
        _gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        LineHeight = GetLineHeight(default);
    }

    public double LineHeight { get; }

    public double MeasureWidth(string text) => MeasureWidth(text, default);

    public double MeasureWidth(string text, TextStyle style)
    {
        var width = 0.0;
        foreach (var (part, font) in _fonts.Runs(text, style))
            width += _gfx.MeasureString(part, font).Width;
        return width / PdfReportRenderer.PointsPerMm;
    }

    public double GetLineHeight(TextStyle style) =>
        _fonts.Get(style).GetHeight() / PdfReportRenderer.PointsPerMm;

    public void Dispose() => _gfx.Dispose();
}
