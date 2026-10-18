using ClioReport.Model;
using ClioReport.Pdf;
using Xunit;

namespace ClioReport.Tests;

public class PdfReportRendererTests
{
    [Fact]
    public void Render_ProducesPdfBytes()
    {
        // Needs a TrueType font; returns early on machines without Arial.
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(fontPath)) return;

        var definition = new ReportDefinition
        {
            Version = 1,
            Bands =
            {
                new BandDefinition
                {
                    Type = BandType.ReportHeader,
                    Height = 10,
                    Elements = { new ElementDefinition { Width = 100, Height = 6, Text = "Phiếu xuất kho" } }
                }
            }
        };
        var report = ReportEngine.Render(definition, new DictionaryDataContext());

        using var stream = new MemoryStream();
        PdfReportRenderer.Render(report, stream, new PdfRenderOptions { FontFilePath = fontPath });

        var bytes = stream.ToArray();
        Assert.True(bytes.Length > 100);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }

    [Fact]
    public void Measurer_AndWrappedRender_Work()
    {
        var fontPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        if (!File.Exists(fontPath)) return;

        var options = new PdfRenderOptions { FontFilePath = fontPath };
        using var measurer = new PdfTextMeasurer(options);

        Assert.True(measurer.LineHeight > 0);
        Assert.True(measurer.MeasureWidth("WWWW") > measurer.MeasureWidth("iiii"));
        Assert.True(measurer.MeasureWidth("iiii") > 0);

        var definition = new ReportDefinition
        {
            Version = 1,
            Bands =
            {
                new BandDefinition
                {
                    Type = BandType.ReportHeader,
                    Height = 5,
                    Elements =
                    {
                        new ElementDefinition
                        {
                            Width = 30, Height = 5, Wrap = true,
                            Text = "Một đoạn chữ khá dài cần phải xuống dòng nhiều lần"
                        }
                    }
                }
            }
        };

        var report = ReportEngine.Render(definition, new DictionaryDataContext(), measurer);
        var element = Assert.Single(Assert.Single(report.Pages).Elements);
        Assert.Contains("\n", element.Text);

        using var stream = new MemoryStream();
        PdfReportRenderer.Render(report, stream, options);
        Assert.True(stream.Length > 100);
    }

    private const string PngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEUlEQVR4nGP4z8DA8B+MgBgAHfAD/dPQfSYAAAAASUVORK5CYII=";

    private static string? FontPath()
    {
        var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf");
        return File.Exists(path) ? path : null;
    }

    private static byte[] RenderPdf(string elementJson, object root, PdfRenderOptions options)
    {
        var definition = ReportSerializer.Deserialize($$"""
        {
          "version": 1,
          "bands": [ { "type": "reportHeader", "height": 12, "elements": [ {{elementJson}} ] } ]
        }
        """);
        var report = ReportEngine.Render(definition, new ObjectDataContext(root));

        using var stream = new MemoryStream();
        PdfReportRenderer.Render(report, stream, options);
        return stream.ToArray();
    }

    [Fact]
    public void Barcode_AndImage_AreDrawn()
    {
        var fontPath = FontPath();
        if (fontPath is null) return;
        var options = new PdfRenderOptions { FontFilePath = fontPath };
        var png = Convert.FromBase64String(PngBase64);

        // Baseline draws no text, so no font is embedded: size differences come from the barcode/image only.
        var plain = RenderPdf("""{ "type": "rect", "width": 1, "height": 1 }""", new { }, options);
        var barcode = RenderPdf("""{ "type": "barcode", "width": 60, "height": 8, "text": "PXK-20261018-0001" }""", new { }, options);
        var image = RenderPdf("""{ "type": "image", "width": 20, "height": 10, "expression": "logo" }""", new { Logo = png }, options);

        var qr = RenderPdf("""{ "type": "qrcode", "width": 20, "height": 20, "text": "PXK-20261018-0001" }""", new { }, options);

        Assert.True(qr.Length > plain.Length);
        Assert.True(barcode.Length > plain.Length);
        Assert.True(image.Length > plain.Length);
    }

    [Fact]
    public void ImagePath_WithoutImageRoot_Throws()
    {
        var fontPath = FontPath();
        if (fontPath is null) return;

        Assert.Throws<InvalidOperationException>(() => RenderPdf(
            """{ "type": "image", "width": 20, "height": 10, "text": "logo.png" }""",
            new { },
            new PdfRenderOptions { FontFilePath = fontPath }));
    }

    [Fact]
    public void ImagePath_MustStayInsideImageRoot()
    {
        var fontPath = FontPath();
        if (fontPath is null) return;

        var root = Path.Combine(Path.GetTempPath(), "clioreport-" + Guid.NewGuid().ToString("N"));
        var images = Path.Combine(root, "images");
        Directory.CreateDirectory(images);
        try
        {
            File.WriteAllBytes(Path.Combine(images, "logo.png"), Convert.FromBase64String(PngBase64));
            File.WriteAllBytes(Path.Combine(root, "secret.png"), Convert.FromBase64String(PngBase64));
            var options = new PdfRenderOptions { FontFilePath = fontPath, ImageRoot = images };

            var ok = RenderPdf("""{ "type": "image", "width": 20, "height": 10, "text": "logo.png" }""", new { }, options);

            Assert.True(ok.Length > 100);
            Assert.Throws<InvalidOperationException>(() => RenderPdf(
                """{ "type": "image", "width": 20, "height": 10, "text": "../secret.png" }""", new { }, options));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
