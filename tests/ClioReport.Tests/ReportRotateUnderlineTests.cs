using Xunit;

namespace ClioReport.Tests;

public class ReportRotateUnderlineTests
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
    public void UnderlineAndRotate_AreCarried()
    {
        var e = Render("""{ "width": 20, "height": 5, "text": "x", "underline": true, "rotate": 270 }""");

        Assert.True(e.Underline);
        Assert.Equal(270, e.Rotate);
    }

    [Fact]
    public void Defaults_AreNotUnderlinedNotRotated()
    {
        var e = Render("""{ "width": 20, "height": 5, "text": "x" }""");

        Assert.False(e.Underline);
        Assert.Equal(0, e.Rotate);
    }

    [Theory]
    [InlineData(45)]
    [InlineData(180)]
    [InlineData(-90)]
    public void Rotate_OtherThan0_90_270_IsRejected(int rotate)
    {
        Assert.Throws<ReportException>(() => Render($$"""{ "width": 20, "height": 5, "text": "x", "rotate": {{rotate}} }"""));
    }
}
