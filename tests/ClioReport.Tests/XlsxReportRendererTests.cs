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

        string At(string text) => cells.Single(c => c.Value.Element(Ns + "is") is { } s && s.Value == text).Key;
        static int Row(string address) => int.Parse(new string(address.Where(char.IsDigit).ToArray()));
        static string Col(string address) => new string(address.Where(char.IsLetter).ToArray());

        Assert.Equal("A", Col(At("Stock <list> & more")));
        var code = At("Code");
        var qty = At("Qty");
        Assert.Equal(Row(code), Row(qty));                          // header cells share a row
        Assert.Equal("A", Col(code));
        Assert.Equal("Q", Col(qty));                                // a 40 mm column is 16 grid columns of 2.5 mm
        Assert.Equal(Row(code) + 1, Row(At("0012")));               // leading zero stays text
        Assert.Equal(Row(code) + 2, Row(At("B-2")));

        var number = cells.Values.Single(c => (string?)c.Attribute("r") == "Q" + (Row(code) + 1));
        Assert.Null(number.Attribute("t"));                          // a plain number is a numeric cell
        Assert.Equal("1500.5", Text(number));
        Assert.Equal("7", Text(cells["Q" + (Row(code) + 2)]));
    }

    [Fact]
    public void Render_GridLinesAreOffUnlessAsked()
    {
        using var off = new ZipArchive(new MemoryStream(XlsxReportRenderer.ToBytes(Table())));
        using var on = new ZipArchive(new MemoryStream(XlsxReportRenderer.ToBytes(Table(), new XlsxRenderOptions { ShowGridLines = true })));
        string View(ZipArchive z) { using var s = z.GetEntry("xl/worksheets/sheet1.xml")!.Open(); return (string?)XDocument.Load(s).Descendants(Ns + "sheetView").Single().Attribute("showGridLines") ?? "1"; }
        Assert.Equal("0", View(off));
        Assert.Equal("1", View(on));
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
    public void Render_PagesOfSeveralRenders_StackInListOrder()
    {
        // Two renders both start at page 1; joined into one report they must not land on the same rows.
        var first = Table();
        var second = Table();
        var joined = new RenderedReport(first.PageWidth, first.PageHeight, first.Pages.Concat(second.Pages).ToList());

        var cells = Cells(XlsxReportRenderer.ToBytes(joined));
        var rows = cells.Where(c => c.Value.Element(Ns + "is") is { } s && s.Value == "B-2")
            .Select(c => int.Parse(new string(c.Key.Where(char.IsDigit).ToArray())))
            .ToList();

        Assert.Equal(2, rows.Count);
        Assert.NotEqual(rows[0], rows[1]);
    }

    [Fact]
    public void Render_EmptyReport_StillValid()
    {
        var bytes = XlsxReportRenderer.ToBytes(new RenderedReport(210, 297, new List<RenderedPage>()));
        Assert.Empty(Cells(bytes));
    }
}
