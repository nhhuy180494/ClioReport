using ClioReport.Model;
using Xunit;

namespace ClioReport.Tests;

public class ReportEngineTests
{
    // Page 50 high, margin 5, page header 5, page footer 5 => body from y=10 to y=40.
    private const string Json = """
    {
      "version": 1,
      "dataSource": "items",
      "page": { "width": 210, "height": 50, "margin": 5 },
      "groups": [ { "name": "kho", "expression": "kho" } ],
      "bands": [
        { "type": "pageHeader", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Trang ' + PageNumber + '/' + PageCount" } ] },
        { "type": "reportHeader", "height": 10, "elements": [
          { "x": 0, "y": 0, "width": 100, "height": 10, "expression": "title" } ] },
        { "type": "groupHeader", "group": "kho", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Kho ' + kho" } ] },
        { "type": "detail", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 30, "height": 5, "expression": "code" },
          { "x": 30, "y": 0, "width": 20, "height": 5, "expression": "qty" } ] },
        { "type": "groupFooter", "group": "kho", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Tong kho: ' + tongKho" } ] },
        { "type": "reportFooter", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Tong cong: ' + tong" } ] },
        { "type": "pageFooter", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "text": "footer" } ] }
      ],
      "aggregates": [
        { "name": "tongKho", "function": "sum", "expression": "qty", "group": "kho" },
        { "name": "tong", "function": "sum", "expression": "qty" }
      ]
    }
    """;

    private static DictionaryDataContext Data(params (string Kho, string Code, int Qty)[] items)
    {
        var root = new DictionaryDataContext(new() { ["title"] = "PHIEU XUAT KHO" });
        root.Rows["items"] = items
            .Select(i => new DictionaryDataContext(new() { ["kho"] = i.Kho, ["code"] = i.Code, ["qty"] = i.Qty }))
            .ToList();
        return root;
    }

    private static IEnumerable<string> Texts(RenderedPage page) => page.Elements.Select(e => e.Text);

    [Fact]
    public void Render_GroupsAggregatesAndPagination()
    {
        var def = ReportSerializer.Deserialize(Json);

        var result = ReportEngine.Render(def, Data(("A", "a1", 2), ("A", "a2", 3), ("B", "b1", 5)));

        Assert.Equal(2, result.Pages.Count);

        var page1 = result.Pages[0];
        var expected = new[]
        {
            "Trang 1/2", "PHIEU XUAT KHO", "Kho A", "a1", "2", "a2", "3", "Tong kho: 5", "footer"
        };
        Assert.Equal(expected.OrderBy(t => t, StringComparer.Ordinal), Texts(page1).OrderBy(t => t, StringComparer.Ordinal));

        var page2 = result.Pages[1];
        Assert.Contains("Trang 2/2", Texts(page2));
        Assert.Contains("Kho B", Texts(page2));
        Assert.Contains("Tong kho: 5", Texts(page2));
        Assert.Contains("Tong cong: 10", Texts(page2));
    }

    [Fact]
    public void Render_PositionsAreAbsoluteInMillimetres()
    {
        var def = ReportSerializer.Deserialize(Json);

        var result = ReportEngine.Render(def, Data(("A", "a1", 2), ("A", "a2", 3), ("B", "b1", 5)));
        var page1 = result.Pages[0];

        Assert.Contains(page1.Elements, e => e.Text == "a1" && e.X == 5 && e.Y == 25);
        Assert.Contains(page1.Elements, e => e.Text == "2" && e.X == 35 && e.Y == 25);
        Assert.Contains(page1.Elements, e => e.Text == "footer" && e.Y == 40);
        Assert.Contains(page1.Elements, e => e.Text == "Trang 1/2" && e.Y == 5);
    }

    [Fact]
    public void Render_NoRows_StillProducesHeaderAndFooter()
    {
        var def = ReportSerializer.Deserialize(Json);

        var result = ReportEngine.Render(def, Data());

        var page = Assert.Single(result.Pages);
        Assert.Contains("PHIEU XUAT KHO", Texts(page));
        Assert.Contains("Tong cong: 0", Texts(page));
    }

    [Fact]
    public void Render_CarriesAlignFromDefinition()
    {
        var def = ReportSerializer.Deserialize("""
        {
          "version": 1,
          "bands": [
            { "type": "reportHeader", "height": 5, "elements": [
              { "width": 50, "height": 5, "align": "right", "text": "a" },
              { "width": 50, "height": 5, "text": "b" } ] }
          ]
        }
        """);

        var page = Assert.Single(ReportEngine.Render(def, new DictionaryDataContext()).Pages);

        Assert.Equal(TextAlign.Right, page.Elements.Single(e => e.Text == "a").Align);
        Assert.Equal(TextAlign.Left, page.Elements.Single(e => e.Text == "b").Align);
    }

    [Fact]
    public void Render_LineAndRect_CarryKindAndNoText()
    {
        var def = ReportSerializer.Deserialize("""
        {
          "version": 1,
          "bands": [
            { "type": "reportHeader", "height": 5, "elements": [
              { "type": "line", "x": 0, "y": 4, "width": 100, "height": 0, "text": "ignored" },
              { "type": "rect", "x": 0, "y": 0, "width": 20, "height": 4, "thickness": 0.5 } ] }
          ]
        }
        """);

        var page = Assert.Single(ReportEngine.Render(def, new DictionaryDataContext()).Pages);

        var line = page.Elements.Single(e => e.Kind == ElementKind.Line);
        Assert.Equal("", line.Text);
        Assert.Equal(0.5, page.Elements.Single(e => e.Kind == ElementKind.Rect).Thickness);
    }

    [Fact]
    public void Render_UnsupportedElementType_Throws()
    {
        var def = new ReportDefinition
        {
            Version = 1,
            Bands =
            {
                new BandDefinition
                {
                    Type = BandType.ReportHeader,
                    Height = 5,
                    Elements = { new ElementDefinition { Type = "circle" } }
                }
            }
        };

        Assert.Throws<ReportException>(() => ReportEngine.Render(def, new DictionaryDataContext()));
    }

    [Fact]
    public void Render_GroupHeader_StaysWithFirstDetailRow()
    {
        // Body is 40 high: header(10) + 2 rows fill 30; the next group needs 20, so it moves to page 2
        // instead of leaving its header alone at the bottom of page 1.
        var def = ReportSerializer.Deserialize("""
        {
          "version": 1,
          "dataSource": "items",
          "page": { "width": 100, "height": 40, "margin": 0 },
          "groups": [ { "name": "kho", "expression": "kho" } ],
          "bands": [
            { "type": "groupHeader", "group": "kho", "height": 10, "elements": [
              { "width": 50, "height": 10, "expression": "'Kho ' + kho" } ] },
            { "type": "detail", "height": 10, "elements": [
              { "width": 50, "height": 10, "expression": "code" } ] }
          ]
        }
        """);

        var result = ReportEngine.Render(def, Data(("A", "a1", 1), ("A", "a2", 1), ("B", "b1", 1)));

        Assert.Equal(2, result.Pages.Count);
        Assert.DoesNotContain("Kho B", Texts(result.Pages[0]));
        Assert.Contains("Kho B", Texts(result.Pages[1]));
        Assert.Contains("b1", Texts(result.Pages[1]));
    }

    [Fact]
    public void Render_PageBreakBefore_StartsEachGroupOnNewPage_ButNotTheFirst()
    {
        var def = ReportSerializer.Deserialize("""
        {
          "version": 1,
          "dataSource": "items",
          "page": { "width": 100, "height": 100, "margin": 0 },
          "groups": [ { "name": "kho", "expression": "kho", "pageBreakBefore": true } ],
          "bands": [
            { "type": "groupHeader", "group": "kho", "height": 10, "elements": [
              { "width": 50, "height": 10, "expression": "'Kho ' + kho" } ] },
            { "type": "detail", "height": 10, "elements": [
              { "width": 50, "height": 10, "expression": "code" } ] }
          ]
        }
        """);

        var result = ReportEngine.Render(def, Data(("A", "a1", 1), ("B", "b1", 1)));

        Assert.Equal(2, result.Pages.Count);
        Assert.Equal(new[] { "Kho A", "a1" }, Texts(result.Pages[0]).OrderBy(t => t, StringComparer.Ordinal));
        Assert.Equal(new[] { "Kho B", "b1" }, Texts(result.Pages[1]).OrderBy(t => t, StringComparer.Ordinal));
    }

    [Fact]
    public void Render_UnknownGroupInBand_Throws()
    {
        var def = new ReportDefinition
        {
            Version = 1,
            Bands = { new BandDefinition { Type = BandType.GroupHeader, Group = "nope", Height = 5 } }
        };

        Assert.Throws<ReportException>(() => ReportEngine.Render(def, new DictionaryDataContext()));
    }

    [Fact]
    public void Render_BandTallerThanPageBody_Throws()
    {
        var def = new ReportDefinition
        {
            Version = 1,
            Bands = { new BandDefinition { Type = BandType.Detail, Height = 500 } }
        };

        Assert.Throws<ReportException>(() => ReportEngine.Render(def, new DictionaryDataContext()));
    }

    [Fact]
    public void Render_BadExpression_ThrowsReportException()
    {
        var def = new ReportDefinition
        {
            Version = 1,
            Bands =
            {
                new BandDefinition
                {
                    Type = BandType.ReportHeader,
                    Height = 5,
                    Elements = { new ElementDefinition { Expression = "1 +" } }
                }
            }
        };

        Assert.Throws<ReportException>(() => ReportEngine.Render(def, new DictionaryDataContext()));
    }
}
