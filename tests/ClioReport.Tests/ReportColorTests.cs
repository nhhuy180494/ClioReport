using Xunit;

namespace ClioReport.Tests;

public class ReportColorTests
{
    private static RenderedElement Render(string elementJson) => Assert.Single(Assert.Single(ReportEngine.Render(
        ReportSerializer.Deserialize($$"""
        {
          "version": 1,
          "bands": [ { "type": "reportHeader", "height": 10, "elements": [ {{elementJson}} ] } ]
        }
        """),
        new DictionaryDataContext()).Pages).Elements);

    [Fact]
    public void ColorFillAndBorder_AreCarried()
    {
        var e = Render("""{ "width": 20, "height": 5, "text": "x", "color": "#C00", "fill": "#EEEEEE", "border": true }""");

        Assert.Equal("#C00", e.Color);
        Assert.Equal("#EEEEEE", e.Fill);
        Assert.True(e.Border);
    }

    [Fact]
    public void Defaults_AreNoColorNoFillNoBorder()
    {
        var e = Render("""{ "width": 20, "height": 5, "text": "x" }""");

        Assert.Null(e.Color);
        Assert.Null(e.Fill);
        Assert.False(e.Border);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    [InlineData("123456")]
    public void InvalidColor_IsRejected(string color) =>
        Assert.Throws<ReportException>(() => Render($$"""{ "width": 20, "height": 5, "text": "x", "fill": "{{color}}" }"""));

    [Fact]
    public void Padding_IsCarried_AndNarrowsTheWrapWidth()
    {
        var def = ReportSerializer.Deserialize("""
        {
          "version": 1,
          "bands": [ { "type": "reportHeader", "height": 5, "elements": [
            { "width": 24, "height": 5, "wrap": true, "padding": 2, "text": "aaaa bbbb" } ] } ]
        }
        """);

        // 24 mm box minus 2 * 2 mm padding = 20 mm = 10 characters: "aaaa bbbb" (9) still fits.
        var fits = Assert.Single(Assert.Single(ReportEngine.Render(def, new DictionaryDataContext(), new FakeTextMeasurer()).Pages).Elements);
        Assert.Equal(2, fits.Padding);
        Assert.Equal("aaaa bbbb", fits.Text);

        def.Bands[0].Elements[0].Padding = 4; // 16 mm = 8 characters: wraps
        var wraps = Assert.Single(Assert.Single(ReportEngine.Render(def, new DictionaryDataContext(), new FakeTextMeasurer()).Pages).Elements);
        Assert.Equal("aaaa\nbbbb", wraps.Text);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(10)]
    public void InvalidPadding_IsRejected(double padding) =>
        Assert.Throws<ReportException>(() => Render($$"""{ "width": 20, "height": 5, "text": "x", "padding": {{padding}} }"""));
}
