using Xunit;

namespace ClioReport.Tests;

public class ReportTableListTests
{
    private static RenderedReport Render(string bandsJson, object? data = null, ITextMeasurer? measurer = null) =>
        ReportEngine.Render(ReportSerializer.Deserialize($$"""
        {
          "version": 1,
          "dataSource": "docs",
          "page": { "width": 210, "height": 297, "margin": 10 },
          "bands": [ {{bandsJson}} ]
        }
        """), new ObjectDataContext(data ?? new { }), measurer);

    private static List<RenderedElement> Elements(RenderedReport r) => r.Pages.SelectMany(p => p.Elements).ToList();

    [Fact]
    public void StaticTable_MakesHeaderAndRows_AsCells()
    {
        var r = Render("""
          { "type": "reportHeader", "height": 5, "elements": [ {
            "type": "table", "x": 0, "y": 0, "width": 100, "rowHeight": 6, "border": true, "fill": "#EEEEEE",
            "columns": [ { "header": "A", "width": 40 }, { "header": "B", "width": 0, "align": "right" } ],
            "rows": [ [ "a1", "b1" ], [ "a2", "=1+2" ] ] } ] }
          """);

        var cells = Elements(r);

        Assert.Equal(new[] { "A", "B", "a1", "b1", "a2", "3" }, cells.Select(c => c.Text));
        Assert.Equal(new[] { 10.0, 50.0 }, cells.Take(2).Select(c => c.X));
        Assert.Equal(60, cells[1].Width, 3);                  // the free column takes the rest
        Assert.Equal(new[] { 10.0, 16.0, 22.0 }, cells.Where(c => c.X == 10).Select(c => c.Y));
        Assert.All(cells, c => Assert.True(c.Border));
        Assert.Equal("#EEEEEE", cells[0].Fill);              // header is filled
        Assert.True(cells[0].Style.Bold);
        Assert.Null(cells[2].Fill);
        Assert.Equal(TextAlignOf(cells[1]), TextAlignOf(cells[3]));
    }

    [Fact]
    public void Table_TotalRow_SumsColumns_AndLabelsFirstPlainColumn()
    {
        var data = new { lines = new[] { new { name = "a", qty = 2, amount = 1500.4m }, new { name = "b", qty = 3, amount = 2000m } } };
        var r = Render("""
          { "type": "reportHeader", "height": 5, "elements": [ {
            "type": "table", "x": 0, "y": 0, "width": 100, "rowHeight": 6, "showHeader": false, "fill": "#EEEEEE", "totalLabel": "Total",
            "source": "lines",
            "columns": [ { "header": "Name", "expression": "name", "width": 40 },
                         { "header": "Qty", "expression": "qty", "width": 20, "total": "sum" },
                         { "header": "Amount", "expression": "amount", "total": "sum", "totalFormat": "N0" } ] } ] }
          """, data);

        var cells = Elements(r);

        Assert.Equal(new[] { "a", "2", "1500.4", "b", "3", "2000", "Total", "5", "3,500" }, cells.Select(c => c.Text));
        var last = cells.Skip(6).ToList();
        Assert.All(last, c => Assert.True(c.Style.Bold));
        Assert.All(last, c => Assert.Equal("#EEEEEE", c.Fill));
        Assert.Equal(cells[0].Y + 12, last[0].Y, 3);
    }

    [Fact]
    public void Table_TotalOnFixedRows_IsRejected()
    {
        var ex = Assert.Throws<ReportException>(() => Render("""
          { "type": "reportHeader", "height": 5, "elements": [ {
            "type": "table", "x": 0, "y": 0, "width": 100,
            "columns": [ { "header": "A", "expression": "x", "total": "sum" } ], "rows": [ [ "1" ] ] } ] }
          """));
        Assert.Contains("source", ex.Message);
    }

    private static Model.TextAlign TextAlignOf(RenderedElement e) => e.Align;

    [Fact]
    public void Table_GrowsItsBand_AndPushesNextBands()
    {
        var r = Render("""
          { "type": "reportHeader", "height": 5, "elements": [ {
            "type": "table", "x": 0, "y": 2, "width": 100, "rowHeight": 6, "showHeader": false,
            "columns": [ { "header": "A" } ], "rows": [ ["1"], ["2"], ["3"] ] } ] },
          { "type": "reportFooter", "height": 5, "elements": [ { "width": 20, "height": 5, "text": "end" } ] }
          """);

        var end = Elements(r).Single(e => e.Text == "end");

        Assert.Equal(10 + 2 + 18, end.Y, 3);   // margin + table y + three 6 mm rows
    }

    [Fact]
    public void Table_FromSource_EvaluatesColumnsPerRow_AndAltFill()
    {
        var data = new { docs = new[] { new { code = "X1", qty = 5 }, new { code = "X2", qty = 7 }, new { code = "X3", qty = 9 } } };
        var r = Render("""
          { "type": "reportHeader", "height": 5, "elements": [ {
            "type": "table", "x": 0, "y": 0, "width": 100, "showHeader": false, "altFill": "#DDDDDD", "source": "docs",
            "columns": [ { "expression": "code", "width": 40 }, { "expression": "qty * 2", "width": 40 }, { "text": "pcs" } ] } ] }
          """, data);

        var cells = Elements(r);

        Assert.Equal(new[] { "X1", "10" }, cells.Take(2).Select(c => c.Text));
        Assert.Equal(new[] { "X1", "X2", "X3" }, cells.Where(c => c.X == 10).Select(c => c.Text));
        Assert.Equal(new[] { null, "#DDDDDD", null }, cells.Where(c => c.X == 10).Select(c => c.Fill));
    }

    [Fact]
    public void Table_Wrap_GrowsTheRow()
    {
        var r = Render("""
          { "type": "reportHeader", "height": 5, "elements": [ {
            "type": "table", "x": 0, "y": 0, "width": 40, "rowHeight": 5, "wrap": true, "showHeader": false,
            "columns": [ { "width": 20 }, { "width": 20 } ], "rows": [ [ "aaaa bbbb cccc dddd", "x" ] ] } ] }
          """, measurer: new FakeTextMeasurer());

        var cells = Elements(r);

        Assert.True(cells[0].Height > 5);
        Assert.Equal(cells[0].Height, cells[1].Height, 3);   // the whole row is as tall as its tallest cell
        Assert.Contains("\n", cells[0].Text);
    }

    [Fact]
    public void List_RepeatsItems_FromNestedRows()
    {
        var data = new
        {
            docs = new[]
            {
                new { no = "D1", lines = new[] { new { name = "a", qty = 1 }, new { name = "b", qty = 2 } } },
                new { no = "D2", lines = new[] { new { name = "c", qty = 3 } } }
            }
        };
        var r = ReportEngine.Render(ReportSerializer.Deserialize("""
        {
          "version": 1, "dataSource": "docs", "page": { "margin": 10 },
          "bands": [ { "type": "detail", "height": 8, "elements": [
            { "x": 0, "y": 0, "width": 30, "height": 5, "expression": "no" },
            { "type": "list", "x": 30, "y": 0, "width": 100, "itemHeight": 6, "source": "lines", "items": [
              { "x": 0, "y": 0, "width": 40, "height": 5, "expression": "name" },
              { "x": 40, "y": 0, "width": 20, "height": 5, "expression": "qty + 100" } ] } ] },
            { "type": "reportFooter", "height": 5, "elements": [ { "width": 10, "height": 5, "text": "end" } ] } ]
        }
        """), new ObjectDataContext(data));

        var all = Elements(r);

        Assert.Equal(new[] { "D1", "a", "101", "b", "102", "D2", "c", "103" }, all.Where(e => e.Text != "end").Select(e => e.Text));
        var b = all.Single(e => e.Text == "b");
        Assert.Equal(10 + 6, b.Y, 3);                       // second item sits one itemHeight lower
        Assert.Equal(10 + 30 + 40 - 40, b.X, 3);
        // D1's band grew to hold two 6 mm items; D2's band is back to 8 mm.
        var d2 = all.Single(e => e.Text == "D2");
        Assert.Equal(10 + 12, d2.Y, 3);
        Assert.Equal(10 + 12 + 8, all.Single(e => e.Text == "end").Y, 3);
    }

    [Fact]
    public void List_Across_PlacesItemsInARow()
    {
        var data = new { docs = new[] { new { v = "1" }, new { v = "2" }, new { v = "3" } } };
        var r = Render("""
          { "type": "reportHeader", "height": 5, "elements": [ {
            "type": "list", "x": 0, "y": 0, "width": 100, "itemHeight": 10, "across": 2, "gap": 10, "source": "docs",
            "items": [ { "x": 1, "y": 2, "width": 30, "height": 5, "expression": "v" } ] } ] }
          """, data);

        var items = Elements(r);

        Assert.Equal(new[] { 11.0, 66.0, 11.0 }, items.Select(e => e.X));   // item width (100-10)/2 = 45, plus gap 10 (also between rows)
        Assert.Equal(new[] { 12.0, 12.0, 32.0 }, items.Select(e => e.Y));
    }

    [Fact]
    public void Tables_And_Lists_Validate()
    {
        const string t = """{ "type": "reportHeader", "height": 5, "elements": [ {0} ] }""";
        Assert.Throws<ReportException>(() => Render(t.Replace("{0}", """{ "type": "table", "width": 50, "columns": [], "rows": [] }""")));
        Assert.Throws<ReportException>(() => Render(t.Replace("{0}", """{ "type": "table", "width": 50, "columns": [ {} ] }""")));
        Assert.Throws<ReportException>(() => Render(t.Replace("{0}", """{ "type": "table", "width": 50, "columns": [ { "width": 60 } ], "rows": [] }""")));
        Assert.Throws<ReportException>(() => Render(t.Replace("{0}", """{ "type": "list", "width": 50, "source": "x", "items": [] }""")));
        Assert.Throws<ReportException>(() => Render(t.Replace("{0}", """{ "type": "list", "width": 50, "items": [ { "width": 5, "height": 5 } ] }""")));
    }

    [Fact]
    public void VerticalLine_WithZeroWidth_IsAllowed()
    {
        var r = Render("""{ "type": "reportHeader", "height": 20, "elements": [ { "type": "line", "x": 5, "y": 0, "width": 0, "height": 15 } ] }""");

        Assert.Single(Elements(r));
    }
}
