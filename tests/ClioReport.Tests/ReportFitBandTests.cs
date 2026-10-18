using Xunit;

namespace ClioReport.Tests;

public class ReportFitBandTests
{
    private static RenderedPage Render(string elements) => Assert.Single(ReportEngine.Render(
        ReportSerializer.Deserialize($$"""
        {
          "version": 1,
          "page": { "width": 200, "height": 100, "margin": 0 },
          "bands": [ { "type": "reportHeader", "height": 5, "elements": [ {{elements}} ] } ]
        }
        """),
        new DictionaryDataContext(), new FakeTextMeasurer()).Pages);

    [Fact]
    public void FitBand_StretchesToTheGrownBand()
    {
        // 20 mm box = 10 characters; "aaaa bbbb cccc" wraps to 2 lines (10 mm), the band grows from 5 to 10.
        var page = Render("""
            { "x": 0, "y": 0, "width": 20, "height": 5, "wrap": true, "text": "aaaa bbbb cccc" },
            { "x": 20, "y": 0, "width": 20, "height": 5, "text": "short", "border": true, "fitBand": true },
            { "x": 40, "y": 1, "width": 20, "height": 2, "text": "tall", "fitBand": true },
            { "x": 60, "y": 0, "width": 20, "height": 5, "text": "plain" }
            """);

        Assert.Equal(10, page.Elements.Single(e => e.Text == "short").Height);
        Assert.Equal(9, page.Elements.Single(e => e.Text == "tall").Height);   // band 10 - y 1
        Assert.Equal(5, page.Elements.Single(e => e.Text == "plain").Height);
    }

    [Fact]
    public void FitBand_NeverShrinksAnElement()
    {
        var page = Render("""{ "x": 0, "y": 0, "width": 20, "height": 8, "text": "x", "fitBand": true }""");

        Assert.Equal(8, Assert.Single(page.Elements).Height);
    }

    [Fact]
    public void VAlign_IsCarried()
    {
        var page = Render("""{ "x": 0, "y": 0, "width": 20, "height": 5, "text": "x", "vAlign": "middle" }""");

        Assert.Equal(Model.VerticalAlign.Middle, Assert.Single(page.Elements).VAlign);
    }
}
