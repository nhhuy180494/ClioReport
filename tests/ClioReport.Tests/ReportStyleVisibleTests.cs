using Xunit;

namespace ClioReport.Tests;

public class ReportStyleVisibleTests
{
    /// <summary>Plain text 2 mm/char, bold 3 mm/char and a 10 pt size doubles the line height.</summary>
    private sealed class StyledMeasurer : ITextMeasurer
    {
        public double MeasureWidth(string text) => text.Length * 2;
        public double LineHeight => 5;
        public double MeasureWidth(string text, TextStyle style) => text.Length * (style.Bold ? 3 : 2);
        public double GetLineHeight(TextStyle style) => style.FontSize > 0 ? style.FontSize / 2 : 5;
    }

    private static RenderedPage Render(string json, DictionaryDataContext? data = null, ITextMeasurer? measurer = null) =>
        Assert.Single(ReportEngine.Render(ReportSerializer.Deserialize(json), data ?? new DictionaryDataContext(), measurer).Pages);

    private static string Wrapped(string flags) => $$"""
    {
      "version": 1,
      "page": { "width": 200, "height": 100, "margin": 0 },
      "bands": [ { "type": "reportHeader", "height": 5, "elements": [
        { "x": 0, "y": 0, "width": 20, "height": 5, "wrap": true, {{flags}} "text": "aaaa bbbb" } ] } ]
    }
    """;

    [Fact]
    public void Style_IsCarriedToTheRenderedElement()
    {
        var element = Assert.Single(Render(Wrapped(""" "bold": true, "italic": true, "fontSize": 14, """), measurer: new StyledMeasurer()).Elements);

        Assert.Equal(new TextStyle(true, true, 14), element.Style);
    }

    [Fact]
    public void Wrap_UsesTheStyleToMeasure()
    {
        var plain = Assert.Single(Render(Wrapped(""), measurer: new StyledMeasurer()).Elements);
        var bold = Assert.Single(Render(Wrapped(""" "bold": true, """), measurer: new StyledMeasurer()).Elements);

        Assert.Equal("aaaa bbbb", plain.Text);     // 9 chars * 2 mm = 18 mm, fits 20 mm
        Assert.Equal("aaaa\nbbbb", bold.Text);     // 9 chars * 3 mm = 27 mm, wraps
    }

    [Fact]
    public void Wrap_UsesTheStyleLineHeight()
    {
        var element = Assert.Single(Render(Wrapped(""" "fontSize": 20, """), measurer: new StyledMeasurer()).Elements);

        Assert.Equal(10, element.LineHeight);
    }

    [Fact]
    public void Measurer_WithoutStyleSupport_StillWorks()
    {
        var element = Assert.Single(Render(Wrapped(""" "bold": true, """), measurer: new FakeTextMeasurer()).Elements);

        Assert.Equal("aaaa bbbb", element.Text);
    }

    [Fact]
    public void NegativeFontSize_IsRejected() =>
        Assert.Throws<ReportException>(() => Render(Wrapped(""" "fontSize": -1, """).Replace("\"wrap\": true,", "")));

    private const string VisibleJson = """
    {
      "version": 1,
      "dataSource": "items",
      "page": { "width": 200, "height": 100, "margin": 0 },
      "bands": [
        { "type": "pageHeader", "height": 5, "visible": "showHeader", "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "text": "HEADER" } ] },
        { "type": "detail", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 30, "height": 5, "expression": "code" } ] },
        { "type": "detail", "height": 5, "visible": "qty > 10", "elements": [
          { "x": 40, "y": 0, "width": 30, "height": 5, "text": "BIG" } ] }
      ]
    }
    """;

    private static DictionaryDataContext Data(bool showHeader)
    {
        var root = new DictionaryDataContext(new() { ["showHeader"] = showHeader });
        root.Rows["items"] = new List<DictionaryDataContext>
        {
            new DictionaryDataContext(new() { ["code"] = "a", ["qty"] = 5 }),
            new DictionaryDataContext(new() { ["code"] = "b", ["qty"] = 50 })
        };
        return root;
    }

    [Fact]
    public void Visible_SkipsDetailBandsWithFalseExpression_AndTheirSpace()
    {
        var page = Render(VisibleJson, Data(true));

        Assert.Equal(new[] { "a", "b", "BIG", "HEADER" }, page.Elements.Select(e => e.Text));
        Assert.Equal(5, page.Elements.Single(e => e.Text == "a").Y);
        Assert.Equal(10, page.Elements.Single(e => e.Text == "b").Y);
        Assert.Equal(15, page.Elements.Single(e => e.Text == "BIG").Y);
    }

    [Fact]
    public void Visible_HiddenPageHeader_KeepsItsSpace()
    {
        var page = Render(VisibleJson, Data(false));

        Assert.DoesNotContain("HEADER", page.Elements.Select(e => e.Text));
        Assert.Equal(5, page.Elements.Single(e => e.Text == "a").Y);
    }

    [Fact]
    public void Visible_BadExpression_IsReportedBeforeLayout() =>
        Assert.Throws<ReportException>(() =>
            Render(VisibleJson.Replace("qty > 10", "qty >"), Data(true)));
}
