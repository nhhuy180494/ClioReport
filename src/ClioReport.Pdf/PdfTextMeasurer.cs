using PdfSharp.Drawing;

namespace ClioReport.Pdf;

/// <summary>
/// Measures text with the same font <see cref="PdfReportRenderer"/> draws with. Pass it to
/// <c>ReportEngine.Render</c> to enable <c>wrap</c>; use the same <see cref="PdfRenderOptions"/> for both.
/// </summary>
public sealed class PdfTextMeasurer : ITextMeasurer, IDisposable
{
    private readonly XGraphics _gfx;
    private readonly XFont _font;

    public PdfTextMeasurer(PdfRenderOptions? options = null)
    {
        options ??= new PdfRenderOptions();
        PdfReportRenderer.EnsureFontResolver(options);

        _font = new XFont(options.FontFamily, options.FontSize);
        _gfx = XGraphics.CreateMeasureContext(new XSize(2000, 2000), XGraphicsUnit.Point, XPageDirection.Downwards);
        LineHeight = _font.GetHeight() / PdfReportRenderer.PointsPerMm;
    }

    public double LineHeight { get; }

    public double MeasureWidth(string text) =>
        _gfx.MeasureString(text, _font).Width / PdfReportRenderer.PointsPerMm;

    public void Dispose() => _gfx.Dispose();
}
