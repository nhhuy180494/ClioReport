using Xunit;

namespace ClioReport.Tests;

public class ReportColumnHeaderTests
{
    // Page 50 high, margin 0, page footer 0: body is 50 mm. Title 5, column header 5, detail 5 each.
    private const string Json = """
    {
      "version": 1,
      "dataSource": "items",
      "page": { "width": 100, "height": 50, "margin": 0 },
      "bands": [
        { "type": "reportHeader", "height": 5, "elements": [ { "x": 0, "y": 0, "width": 50, "height": 5, "text": "TITLE" } ] },
        { "type": "columnHeader", "height": 5, "visible": "showHeader", "elements": [ { "x": 0, "y": 0, "width": 50, "height": 5, "text": "COLS" } ] },
        { "type": "detail", "height": 5, "elements": [ { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "code" } ] }
      ]
    }
    """;

    private static DictionaryDataContext Data(int rows, bool showHeader = true)
    {
        var root = new DictionaryDataContext(new() { ["showHeader"] = showHeader });
        root.Rows["items"] = Enumerable.Range(1, rows)
            .Select(i => new DictionaryDataContext(new() { ["code"] = "r" + i }))
            .ToList();
        return root;
    }

    private static RenderedReport Render(int rows, bool showHeader = true) =>
        ReportEngine.Render(ReportSerializer.Deserialize(Json), Data(rows, showHeader));

    [Fact]
    public void FirstPage_HasColumnHeaderAfterReportHeader()
    {
        var page = Assert.Single(Render(2).Pages);

        Assert.Equal(new[] { "TITLE", "COLS", "r1", "r2" }, page.Elements.Select(e => e.Text));
        Assert.Equal(new[] { 0.0, 5, 10, 15 }, page.Elements.Select(e => e.Y));
    }

    [Fact]
    public void EveryNewPage_RepeatsTheColumnHeader()
    {
        // Page 1 holds TITLE + COLS + 8 rows (50 mm); row 9 starts page 2 below the repeated header.
        var result = Render(10);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(10, result.Pages[0].Elements.Count);
        Assert.Equal(new[] { "COLS", "r9", "r10" }, result.Pages[1].Elements.Select(e => e.Text));
        Assert.Equal(new[] { 0.0, 5, 10 }, result.Pages[1].Elements.Select(e => e.Y));
    }

    [Fact]
    public void HiddenColumnHeader_IsSkipped()
    {
        var page = Assert.Single(Render(2, showHeader: false).Pages);

        Assert.DoesNotContain("COLS", page.Elements.Select(e => e.Text));
    }

    [Fact]
    public void ColumnHeaderTallerThanTheBody_IsRejected() =>
        Assert.Throws<ReportException>(() => ReportEngine.Render(
            ReportSerializer.Deserialize(Json.Replace("\"type\": \"columnHeader\", \"height\": 5", "\"type\": \"columnHeader\", \"height\": 50")),
            Data(1)));
}
