using System.IO.Compression;
using System.Xml.Linq;
using ClioReport.Model;
using ClioReport.Xlsx;
using Xunit;

namespace ClioReport.Tests;

public class XlsxReportRendererTests
{
    private static readonly XNamespace Ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    private static RenderedReport Table()
    {
        var definition = new ReportDefinition
        {
            Version = 1,
            Bands =
            {
                new BandDefinition
                {
                    Type = BandType.ReportHeader,
                    Height = 10,
                    Elements = { new ElementDefinition { X = 10, Width = 100, Height = 6, Text = "Stock <list> & more" } }
                },
                new BandDefinition
                {
                    Type = BandType.ReportHeader,
                    Height = 20,
                    Elements =
                    {
                        new ElementDefinition
                        {
                            Type = "table", X = 10, Width = 120, Height = 20,
                            Columns = new List<ColumnDefinition>
                            {
                                new() { Header = "Code", Width = 40 },
                                new() { Header = "Qty", Width = 40, Align = TextAlign.Right }
                            },
                            Rows = new List<List<string>> { new() { "0012", "1500.5" }, new() { "B-2", "7" } }
                        }
                    }
                }
            }
        };
        return ReportEngine.Render(definition, new DictionaryDataContext());
    }

    private static Dictionary<string, XElement> Cells(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var s = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        return XDocument.Load(s).Descendants(Ns + "c").ToDictionary(c => (string)c.Attribute("r")!);
    }

    private static string Text(XElement c) => c.Element(Ns + "v")?.Value ?? c.Descendants(Ns + "t").Single().Value;

    [Fact]
    public void Render_WritesGridWithNumbersAndText()
    {
        var bytes = XlsxReportRenderer.ToBytes(Table());
        var cells = Cells(bytes);

        Assert.Equal("Stock <list> & more", Text(cells["A1"]));
        Assert.Equal("Code", Text(cells["A2"]));
        Assert.Equal("Qty", Text(cells["B2"]));
        Assert.Equal("inlineStr", (string?)cells["A3"].Attribute("t"));   // leading zero stays text
        Assert.Equal("0012", Text(cells["A3"]));
        Assert.Null(cells["B3"].Attribute("t"));                           // plain number
        Assert.Equal("1500.5", Text(cells["B3"]));
        Assert.Equal("B-2", Text(cells["A4"]));
        Assert.Equal("7", Text(cells["B4"]));
    }

    [Fact]
    public void Render_ProducesAWellFormedPackage()
    {
        using var zip = new ZipArchive(new MemoryStream(XlsxReportRenderer.ToBytes(Table(), new XlsxRenderOptions { SheetName = "A/B:C" })));
        foreach (var name in new[] { "[Content_Types].xml", "_rels/.rels", "xl/workbook.xml", "xl/_rels/workbook.xml.rels", "xl/styles.xml", "xl/worksheets/sheet1.xml" })
        {
            using var s = zip.GetEntry(name)!.Open();
            Assert.NotNull(XDocument.Load(s).Root);      // throws when not well-formed
        }
        using var wb = zip.GetEntry("xl/workbook.xml")!.Open();
        Assert.Equal("ABC", (string?)XDocument.Load(wb).Descendants(Ns + "sheet").Single().Attribute("name"));
    }

    [Fact]
    public void Render_EmptyReport_StillValid()
    {
        var bytes = XlsxReportRenderer.ToBytes(new RenderedReport(210, 297, new List<RenderedPage>()));
        Assert.Empty(Cells(bytes));
    }
}
