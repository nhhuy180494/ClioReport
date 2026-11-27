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

    private static XElement SheetXml(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        using var s = zip.GetEntry("xl/worksheets/sheet1.xml")!.Open();
        return XDocument.Load(s).Root!;
    }

    [Fact]
    public void Render_PrintPages_BreaksAfterEveryPageAndUsesThePageSize()
    {
        var first = Table();
        var joined = new RenderedReport(first.PageWidth, first.PageHeight, first.Pages.Concat(Table().Pages).Concat(Table().Pages).ToList());
        var sheet = SheetXml(XlsxReportRenderer.ToBytes(joined));

        var breaks = sheet.Element(Ns + "rowBreaks")!.Elements(Ns + "brk").Select(b => int.Parse((string)b.Attribute("id")!)).ToList();
        Assert.Equal(2, breaks.Count);                                   // between the 3 pages, none after the last
        var lastRow = sheet.Descendants(Ns + "row").Max(r => int.Parse((string)r.Attribute("r")!));
        Assert.Equal(lastRow, breaks[1] + (breaks[1] - breaks[0]));      // pages follow each other with no blank row

        var setup = sheet.Element(Ns + "pageSetup")!;
        Assert.Equal("210mm", (string?)setup.Attribute("paperWidth"));
        Assert.Equal("297mm", (string?)setup.Attribute("paperHeight"));
        var margins = sheet.Element(Ns + "pageMargins")!;
        Assert.Equal("0", (string?)margins.Attribute("header"));
        var left = first.Pages[0].Elements.Min(e => e.X);                // page margin + element X: where the content starts
        Assert.Equal((left / 25.4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture), (string?)margins.Attribute("left"));
    }

    [Fact]
    public void Render_WithoutPrintPages_BlankRowBetweenPagesAndA4()
    {
        var first = Table();
        var joined = new RenderedReport(first.PageWidth, first.PageHeight, first.Pages.Concat(Table().Pages).ToList());
        var single = SheetXml(XlsxReportRenderer.ToBytes(first, new XlsxRenderOptions { PrintPages = false }));
        var sheet = SheetXml(XlsxReportRenderer.ToBytes(joined, new XlsxRenderOptions { PrintPages = false }));

        int Rows(XElement s) => s.Descendants(Ns + "row").Max(r => int.Parse((string)r.Attribute("r")!));
        Assert.Equal(2 * Rows(single) + 1, Rows(sheet));
        Assert.Null(sheet.Element(Ns + "rowBreaks"));
        Assert.Null(sheet.Element(Ns + "pageSetup")!.Attribute("paperWidth"));
        Assert.Equal("0.5", (string?)sheet.Element(Ns + "pageMargins")!.Attribute("left"));
    }

    // Borders of the cell at an address: side name -> style ("thin", "medium", "thick"), only the sides that have one.
    private static Dictionary<string, string> BordersAt(byte[] bytes, string address)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes));
        XElement Load(string name) { using var s = zip.GetEntry(name)!.Open(); return XDocument.Load(s).Root!; }
        var sheet = Load("xl/worksheets/sheet1.xml");
        var styles = Load("xl/styles.xml");
        var cell = sheet.Descendants(Ns + "c").SingleOrDefault(c => (string?)c.Attribute("r") == address);
        if (cell is null) return new();
        var xf = styles.Element(Ns + "cellXfs")!.Elements(Ns + "xf").ElementAt(int.Parse((string)cell.Attribute("s")!));
        var border = styles.Element(Ns + "borders")!.Elements(Ns + "border").ElementAt(int.Parse((string)xf.Attribute("borderId")!));
        return border.Elements()
            .Where(e => e.Attribute("style") is not null)
            .ToDictionary(e => e.Name.LocalName, e => (string)e.Attribute("style")!);
    }

    [Fact]
    public void Render_LinesAndRectangles_BecomeCellBorders()
    {
        // A 50 x 20 mm box split by a horizontal line at 10 mm and a thick vertical line at 25 mm, with one text in the
        // top-left quarter. Grid of 2.5 mm columns from x = 10 (page margin): columns A..T, the vertical line between J and K.
        var definition = new ReportDefinition
        {
            Version = 1,
            Bands =
            {
                new BandDefinition
                {
                    Type = BandType.ReportHeader,
                    Height = 30,
                    Elements =
                    {
                        new ElementDefinition { Type = "rect", X = 0, Y = 0, Width = 50, Height = 20 },
                        new ElementDefinition { Type = "line", X = 0, Y = 10, Width = 50, Height = 0 },
                        new ElementDefinition { Type = "line", X = 25, Y = 0, Width = 0, Height = 20, Thickness = 0.8 },
                        new ElementDefinition { X = 0, Y = 0, Width = 25, Height = 10, Text = "A" }
                    }
                }
            }
        };
        var bytes = XlsxReportRenderer.ToBytes(ReportEngine.Render(definition, new DictionaryDataContext()));

        Assert.Equal("thin", BordersAt(bytes, "A1")["top"]);       // rectangle, top-left corner
        Assert.Equal("thin", BordersAt(bytes, "A1")["left"]);
        Assert.Equal("thick", BordersAt(bytes, "J1")["right"]);    // thick vertical line, left of it
        Assert.Equal("thick", BordersAt(bytes, "K2")["left"]);     // and right of it, lower half too
        Assert.Equal("thin", BordersAt(bytes, "E1")["bottom"]);    // horizontal line at 10 mm
        Assert.Equal("thin", BordersAt(bytes, "E2")["top"]);
        Assert.Equal("thin", BordersAt(bytes, "T2")["right"]);     // empty cell that only carries the rectangle's side
        Assert.Equal("thin", BordersAt(bytes, "T2")["bottom"]);
        Assert.Empty(BordersAt(bytes, "U1"));                      // nothing past the rectangle
    }

    [Fact]
    public void Render_EmptyReport_StillValid()
    {
        var bytes = XlsxReportRenderer.ToBytes(new RenderedReport(210, 297, new List<RenderedPage>()));
        Assert.Empty(Cells(bytes));
    }
}
