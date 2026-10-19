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
                        DrawImage(gfx, e, rect, images, fonts);
                        break;
                    default:
                    {
                        if (e.Fill is not null) gfx.DrawRectangle(BrushOf(e.Fill), rect);
                        if (e.Border) gfx.DrawRectangle(PenOf(e), rect);
                        if (e.Text.Length == 0) break;

                        var rotated = e.Rotate != 0;
                        if (rotated)
                        {
                            // Draw in a box turned 90°: its width is the element height, centred on the element.
                            gfx.Save();
                            var cx = rect.X + rect.Width / 2;
                            var cy = rect.Y + rect.Height / 2;
                            gfx.RotateAtTransform(e.Rotate == 90 ? 90 : -90, new XPoint(cx, cy));
                            rect = new XRect(cx - rect.Height / 2, cy - rect.Width / 2, rect.Height, rect.Width);
                        }

                        var font = fonts.Get(e.Style);
                        rect = new XRect(rect.X + e.Padding * PointsPerMm, rect.Y, rect.Width - 2 * e.Padding * PointsPerMm, rect.Height);
                        var brush = e.Color is null ? XBrushes.Black : BrushOf(e.Color);

                        var lines = e.LineHeight <= 0 ? new[] { e.Text } : e.Text.Split('\n');
                        var step = e.LineHeight <= 0 ? font.GetHeight() : e.LineHeight * PointsPerMm;
                        var block = lines.Length * step;
                        var top = e.VAlign switch
                        {
                            VerticalAlign.Middle => Math.Max(rect.Y, rect.Y + (rect.Height - block) / 2),
                            VerticalAlign.Bottom => Math.Max(rect.Y, rect.Bottom - block),
                            _ => rect.Y
                        };
                        for (var i = 0; i < lines.Length; i++)
                        {
                            if (lines[i].Length == 0) continue;
                            var lineRect = new XRect(rect.X, top + i * step, rect.Width, step);
                            DrawLine(gfx, fonts, lines[i], e, font, brush, lineRect);
                            if (e.Underline) DrawUnderline(gfx, fonts, lines[i], e, font, brush, lineRect);
                        }
                        if (rotated) gfx.Restore();
                        break;
                    }
                }
            }
        }

        document.Save(output, false);
    }

    /// <summary>Draws one line; when it mixes main and fallback fonts each run is placed after the previous one.</summary>
    private static void DrawLine(XGraphics gfx, FontCache fonts, string text, RenderedElement e, XFont font, XBrush brush, XRect rect)
    {
        var runs = fonts.Runs(text, e.Style);
        if (runs.Count <= 1)
        {
            gfx.DrawString(text, runs.Count == 1 ? runs[0].Font : font, brush, rect, FormatOf(e.Align));
            return;
        }

        var widths = runs.Select(r => gfx.MeasureString(r.Text, r.Font).Width).ToList();
        var total = widths.Sum();
        var x = e.Align switch
        {
            TextAlign.Center => rect.X + (rect.Width - total) / 2,
            TextAlign.Right => rect.Right - total,
            _ => rect.X
        };
        for (var i = 0; i < runs.Count; i++)
        {
            gfx.DrawString(runs[i].Text, runs[i].Font, brush, new XRect(x, rect.Y, widths[i] + 1, rect.Height), XStringFormats.TopLeft);
            x += widths[i];
        }
    }

    private static void DrawUnderline(XGraphics gfx, FontCache fonts, string text, RenderedElement e, XFont font, XBrush brush, XRect rect)
    {
        var width = fonts.Runs(text, e.Style).Sum(r => gfx.MeasureString(r.Text, r.Font).Width);
        var left = e.Align switch
        {
            TextAlign.Center => rect.X + (rect.Width - width) / 2,
            TextAlign.Right => rect.Right - width,
            _ => rect.X
        };
        var y = rect.Y + font.GetHeight() * 0.92;
        var pen = new XPen(e.Color is null ? XColors.Black : ColorOf(e.Color), Math.Max(0.4, font.Size / 18));
        gfx.DrawLine(pen, left, y, left + width, y);
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

    private static void DrawImage(XGraphics gfx, RenderedElement e, XRect rect, ImageCache images, FontCache fonts)
    {
        var image = images.Get(e);
        if (image is null || image.PointWidth <= 0 || image.PointHeight <= 0)
        {
            DrawNoImage(gfx, rect, fonts);
            return;
        }

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

    /// <summary>Placeholder for an image element with no picture (empty value, null field, file not found).</summary>
    private static void DrawNoImage(XGraphics gfx, XRect rect, FontCache fonts)
    {
        var grey = XColor.FromArgb(150, 150, 150);
        gfx.DrawRectangle(new XPen(grey, 0.5) { DashStyle = XDashStyle.Dash }, rect);
        var font = fonts.Get(new TextStyle(FontSize: Math.Min(8, Math.Max(4, rect.Height / 3))));
        gfx.DrawString("No image", font, new XSolidBrush(grey), rect, XStringFormats.Center);
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

            // Main font: FontFilePath, else the registered family named like FontFamily (e.g. the bundled fonts), else the
            // system Arial.
            FontSource? regular = null, bold = null, italic = null, boldItalic = null;
            if (options.FontFilePath is null && options.Fonts.TryGetValue(options.FontFamily, out var main))
                (regular, bold, italic, boldItalic) = (main.Regular, main.Bold, main.Italic, main.BoldItalic);
            else
            {
                var path = options.FontFilePath ?? DefaultFontPath();
                if (path is null || !File.Exists(path))
                    throw new InvalidOperationException(
                        "No font available. Set PdfRenderOptions.FontFilePath to a .ttf file that covers your text " +
                        "(e.g. Arial), register fonts in PdfRenderOptions.Fonts (or use ClioReport.Pdf.Fonts), or assign " +
                        "GlobalFontSettings.FontResolver before rendering.");
                regular = path;
                bold = ExistingOrNull(options.BoldFontFilePath);
                italic = ExistingOrNull(options.ItalicFontFilePath);
                boldItalic = ExistingOrNull(options.BoldItalicFontFilePath);
            }

            GlobalFontSettings.FontResolver = new SingleFileFontResolver(
                regular!, bold, italic, boldItalic, options.FallbackFont ?? ExistingOrNull(options.FallbackFontFilePath),
                options.FallbackBoldFont ?? ExistingOrNull(options.FallbackBoldFontFilePath), CheckedFamilies(options));
        }
    }

    private static IReadOnlyDictionary<string, PdfFontFiles> CheckedFamilies(PdfRenderOptions options)
    {
        foreach (var (name, files) in options.Fonts)
            foreach (var source in new[] { files.Regular, files.Bold, files.Italic, files.BoldItalic })
                source?.EnsureExists(name);
        return options.Fonts;
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
