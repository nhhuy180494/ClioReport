using Xunit;

namespace ClioReport.Tests;

public class ReportColumnsTests
{
    // Page 100 x 30, margin 0, 3 columns, gap 5: cell width (100 - 10) / 3 = 30. Cell 10 mm high.
    private const string Json = """
    {
      "version": 1,
      "dataSource": "items",
      "page": { "width": 100, "height": 30, "margin": 0, "columns": 3, "columnGap": 5 },
      "bands": [
        { "type": "reportHeader", "height": 4, "elements": [ { "x": 0, "y": 0, "width": 50, "height": 4, "text": "TITLE" } ] },
        { "type": "detail", "height": 10, "elements": [ { "x": 1, "y": 2, "width": 28, "height": 5, "expression": "code" } ] },
        { "type": "reportFooter", "height": 4, "elements": [ { "x": 0, "y": 0, "width": 50, "height": 4, "text": "END" } ] }
      ]
    }
    """;

    private static RenderedReport Render(int rows, string? json = null)
    {
        var root = new DictionaryDataContext();
        root.Rows["items"] = Enumerable.Range(1, rows)
            .Select(i => new DictionaryDataContext(new() { ["code"] = "r" + i }))
            .ToList();
        return ReportEngine.Render(ReportSerializer.Deserialize(json ?? Json), root);
    }

    [Fact]
    public void Cells_FlowLeftToRightThenDown()
    {
        var page = Assert.Single(Render(4).Pages);

        var cells = page.Elements.Where(e => e.Text.StartsWith('r')).ToList();
        Assert.Equal(new[] { "r1", "r2", "r3", "r4" }, cells.Select(c => c.Text));
        Assert.Equal(new[] { 1.0, 36, 71, 1 }, cells.Select(c => c.X));
        Assert.Equal(new[] { 6.0, 6, 6, 16 }, cells.Select(c => c.Y));   // header 4 + cell y 2, second row +10
    }

    [Fact]
    public void FullWidthBands_StartBelowAPartialRow()
    {
        var page = Assert.Single(Render(4).Pages);

        // Rows: r1-r3 (y 4..14), r4 (y 14..24); footer starts at 24 under the unfinished row.
        Assert.Equal(24, page.Elements.Single(e => e.Text == "END").Y);
    }

    [Fact]
    public void RowsMoveToTheNextPageWhenTheyDoNotFit()
    {
        // Page body 30: title 4 + rows at 4, 14 (ends 24); a third row (24..34) does not fit.
        var result = Render(7);

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(new[] { "r7" }, result.Pages[1].Elements.Where(e => e.Text.StartsWith('r')).Select(e => e.Text));
        Assert.Equal(2, result.Pages[1].Elements.Single(e => e.Text == "r7").Y);
    }

    [Fact]
    public void SeveralColumns_NeedExactlyOneDetailBand() =>
        Assert.Throws<ReportException>(() => Render(1, Json.Replace(
            "{ \"type\": \"reportFooter\"", "{ \"type\": \"detail\", \"height\": 5, \"elements\": [] }, { \"type\": \"reportFooter\"")));

    [Fact]
    public void ColumnsBelowOne_AreRejected() =>
        Assert.Throws<ReportException>(() => Render(1, Json.Replace("\"columns\": 3", "\"columns\": 0")));
}
