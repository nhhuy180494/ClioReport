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
}
