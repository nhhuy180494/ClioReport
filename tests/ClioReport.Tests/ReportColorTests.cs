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
}
