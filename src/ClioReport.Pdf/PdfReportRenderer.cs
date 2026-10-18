using ClioReport.Model;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace ClioReport.Pdf;

/// <summary>Draws a <see cref="RenderedReport"/> to PDF. One font, no clipping.</summary>
public static class PdfReportRenderer
{
    internal const double PointsPerMm = 72.0 / 25.4;
    private static readonly object FontLock = new();

    public static void Render(RenderedReport report, string path, PdfRenderOptions? options = null)
    {
        using var stream = File.Create(path);
        Render(report, stream, options);
    }

    public static void Render(RenderedReport report, Stream output, PdfRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(output);
        options ??= new PdfRenderOptions();
        EnsureFontResolver(options);

        using var document = new PdfDocument();
        var font = new XFont(options.FontFamily, options.FontSize);

        foreach (var rendered in report.Pages)
        {
            var page = document.AddPage();
            page.Width = XUnit.FromMillimeter(report.PageWidth);
            page.Height = XUnit.FromMillimeter(report.PageHeight);

            using var gfx = XGraphics.FromPdfPage(page);
            foreach (var e in rendered.Elements)
            {
                var rect = new XRect(e.X * PointsPerMm, e.Y * PointsPerMm, e.Width * PointsPerMm, e.Height * PointsPerMm);
                switch (e.Kind)
                {
                    case ElementKind.Line:
                        gfx.DrawLine(PenOf(e), rect.Left, rect.Top, rect.Right, rect.Bottom);
                        break;
                    case ElementKind.Rect:
                        gfx.DrawRectangle(PenOf(e), rect);
                        break;
                    default:
                    {
                        if (e.Text.Length == 0) break;

                        if (e.LineHeight <= 0)
                        {
                            gfx.DrawString(e.Text, font, XBrushes.Black, rect, FormatOf(e.Align));
                            break;
                        }

                        var step = e.LineHeight * PointsPerMm;
                        var lines = e.Text.Split('\n');
                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].Length == 0) continue;
                            var lineRect = new XRect(rect.X, rect.Y + i * step, rect.Width, step);
                            gfx.DrawString(lines[i], font, XBrushes.Black, lineRect, FormatOf(e.Align));
                        }
                        break;
                    }
                }
            }
        }

        document.Save(output, false);
    }

    private static XPen PenOf(RenderedElement e) => new(XColors.Black, e.Thickness * PointsPerMm);

    private static XStringFormat FormatOf(TextAlign align) => align switch
    {
        TextAlign.Center => XStringFormats.TopCenter,
        TextAlign.Right => XStringFormats.TopRight,
        _ => XStringFormats.TopLeft
    };

    internal static void EnsureFontResolver(PdfRenderOptions options)
    {
        lock (FontLock)
        {
            if (GlobalFontSettings.FontResolver is not null) return;

            var path = options.FontFilePath ?? DefaultFontPath();
            if (path is null || !File.Exists(path))
                throw new InvalidOperationException(
                    "No font available. Set PdfRenderOptions.FontFilePath to a .ttf file that covers your text " +
                    "(e.g. Arial), or assign GlobalFontSettings.FontResolver before rendering.");

            GlobalFontSettings.FontResolver = new SingleFileFontResolver(path);
        }
    }

    private static string? DefaultFontPath()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "arial.ttf");
    }
}
