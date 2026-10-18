using ClioReport.Model;
using PdfSharp.Drawing;
using PdfSharp.Fonts;
using PdfSharp.Pdf;

namespace ClioReport.Pdf;

/// <summary>Draws a <see cref="RenderedReport"/> to PDF: text, lines, rectangles, Code 128 barcodes, QR codes and images. One font family (regular/bold/italic), no clipping.</summary>
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

        using var images = new ImageCache(options);
        using var document = new PdfDocument();
        var fonts = new FontCache(options);

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
                        if (e.Fill is not null) gfx.DrawRectangle(BrushOf(e.Fill), rect);
                        gfx.DrawRectangle(PenOf(e), rect);
                        break;
                    case ElementKind.Barcode:
                        DrawBarcode(gfx, e, rect);
                        break;
                    case ElementKind.QrCode:
                        DrawQrCode(gfx, e, rect);
                        break;
                    case ElementKind.Image:
                        DrawImage(gfx, e, rect, images);
                        break;
                    default:
                    {
                        if (e.Fill is not null) gfx.DrawRectangle(BrushOf(e.Fill), rect);
                        if (e.Border) gfx.DrawRectangle(PenOf(e), rect);
                        if (e.Text.Length == 0) break;

                        var font = fonts.Get(e.Style);
                        rect = new XRect(rect.X + e.Padding * PointsPerMm, rect.Y, rect.Width - 2 * e.Padding * PointsPerMm, rect.Height);
                        var brush = e.Color is null ? XBrushes.Black : BrushOf(e.Color);

                        if (e.LineHeight <= 0)
                        {
                            gfx.DrawString(e.Text, font, brush, rect, FormatOf(e.Align));
                            break;
                        }

                        var step = e.LineHeight * PointsPerMm;
                        var lines = e.Text.Split('\n');
                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].Length == 0) continue;
                            var lineRect = new XRect(rect.X, rect.Y + i * step, rect.Width, step);
                            gfx.DrawString(lines[i], font, brush, lineRect, FormatOf(e.Align));
                        }
                        break;
                    }
                }
            }
        }

        document.Save(output, false);
    }

    private static void DrawBarcode(XGraphics gfx, RenderedElement e, XRect rect)
    {
        if (e.Text.Length == 0) return;

        var modules = Code128.Encode(e.Text);
        var unit = rect.Width / modules.Length;
        for (var i = 0; i < modules.Length;)
        {
            if (!modules[i])
            {
                i++;
                continue;
            }

            var start = i;
            while (i < modules.Length && modules[i]) i++;
            gfx.DrawRectangle(XBrushes.Black, rect.X + start * unit, rect.Y, (i - start) * unit, rect.Height);
        }
    }

    private static void DrawQrCode(XGraphics gfx, RenderedElement e, XRect rect)
    {
        if (e.Text.Length == 0) return;

        var modules = QrCode.Encode(e.Text);
        var count = modules.GetLength(0);
        var unit = Math.Min(rect.Width, rect.Height) / count;
        var size = unit * count;
        var left = e.Align switch
        {
            TextAlign.Center => rect.X + (rect.Width - size) / 2,
            TextAlign.Right => rect.Right - size,
            _ => rect.X
        };
        for (var y = 0; y < count; y++)
        {
            for (var x = 0; x < count;)
            {
                if (!modules[y, x])
                {
                    x++;
                    continue;
                }

                var start = x;
                while (x < count && modules[y, x]) x++;
                gfx.DrawRectangle(XBrushes.Black, left + start * unit, rect.Y + y * unit, (x - start) * unit, unit);
            }
        }
    }

    private static void DrawImage(XGraphics gfx, RenderedElement e, XRect rect, ImageCache images)
    {
        var image = images.Get(e);
        if (image is null || image.PointWidth <= 0 || image.PointHeight <= 0) return;

        var scale = Math.Min(rect.Width / image.PointWidth, rect.Height / image.PointHeight);
        var width = image.PointWidth * scale;
        var height = image.PointHeight * scale;
        var x = e.Align switch
        {
            TextAlign.Center => rect.X + (rect.Width - width) / 2,
            TextAlign.Right => rect.Right - width,
            _ => rect.X
        };
        gfx.DrawImage(image, x, rect.Y, width, height);
    }

    private static XPen PenOf(RenderedElement e) =>
        new(e.Color is null ? XColors.Black : ColorOf(e.Color), e.Thickness * PointsPerMm);

    private static XSolidBrush BrushOf(string color) => new(ColorOf(color));

    /// <summary>The engine has already validated the colour as #RGB or #RRGGBB.</summary>
    private static XColor ColorOf(string color)
    {
        var hex = color[1..];
        if (hex.Length == 3) hex = string.Concat(hex.Select(c => new string(c, 2)));
        return XColor.FromArgb(
            Convert.ToInt32(hex[..2], 16), Convert.ToInt32(hex[2..4], 16), Convert.ToInt32(hex[4..], 16));
    }

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

            GlobalFontSettings.FontResolver = new SingleFileFontResolver(
                path, ExistingOrNull(options.BoldFontFilePath), ExistingOrNull(options.ItalicFontFilePath),
                ExistingOrNull(options.BoldItalicFontFilePath));
        }
    }

    private static string? ExistingOrNull(string? path)
    {
        if (path is null) return null;
        return File.Exists(path) ? path : throw new FileNotFoundException("Font file not found.", path);
    }

    private static string? DefaultFontPath()
    {
        var dir = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
        return string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "arial.ttf");
    }
}
