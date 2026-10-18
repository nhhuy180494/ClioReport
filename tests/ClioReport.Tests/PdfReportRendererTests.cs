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
}
