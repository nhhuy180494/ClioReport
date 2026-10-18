using ClioReport.Model;
using Xunit;

namespace ClioReport.Tests;

public class ReportWrapTests
{
    private static ReportDefinition Define(string elementJson, int pageHeight = 100) => ReportSerializer.Deserialize($$"""
    {
      "version": 1,
      "page": { "width": 200, "height": {{pageHeight}}, "margin": 0 },
      "bands": [
        { "type": "reportHeader", "height": 5, "elements": [ {{elementJson}} ] },
        { "type": "reportFooter", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 20, "height": 5, "text": "after" } ] }
      ]
    }
    """);

    private static RenderedPage Render(ReportDefinition def, ITextMeasurer? measurer = null) =>
        Assert.Single(ReportEngine.Render(def, new DictionaryDataContext(), measurer).Pages);

    [Fact]
    public void Wrap_BreaksAtWordsAndGrowsBand_PushingNextBand()
    {
        // 20 mm box = 10 characters.
        var def = Define("""{ "x": 0, "y": 0, "width": 20, "height": 5, "wrap": true, "text": "aaaa bbbb cccc" }""");

        var page = Render(def, new FakeTextMeasurer());

        var wrapped = page.Elements.Single(e => e.Text.Contains("aaaa"));
        Assert.Equal("aaaa bbbb\ncccc", wrapped.Text);
        Assert.Equal(5, wrapped.LineHeight);
        Assert.Equal(10, wrapped.Height);
        Assert.Equal(10, page.Elements.Single(e => e.Text == "after").Y);
    }

    [Fact]
    public void Wrap_BreaksOverlongWordByCharacters()
    {
        var def = Define("""{ "x": 0, "y": 0, "width": 10, "height": 5, "wrap": true, "text": "abcdefghij" }""");

        var page = Render(def, new FakeTextMeasurer());

        Assert.Equal("abcde\nfghij", page.Elements.First().Text);
    }

    [Fact]
    public void Wrap_KeepsExplicitNewlines()
    {
        var def = Define("""{ "x": 0, "y": 0, "width": 100, "height": 5, "wrap": true, "text": "ab\ncd" }""");

        var page = Render(def, new FakeTextMeasurer());

        Assert.Equal("ab\ncd", page.Elements.First().Text);
    }

    [Fact]
    public void NoWrap_LeavesTextAndHeightAlone()
    {
        var def = Define("""{ "x": 0, "y": 0, "width": 20, "height": 5, "text": "aaaa bbbb cccc" }""");

        var page = Render(def, new FakeTextMeasurer());

        var element = page.Elements.First();
        Assert.Equal("aaaa bbbb cccc", element.Text);
        Assert.Equal(0, element.LineHeight);
        Assert.Equal(5, page.Elements.Single(e => e.Text == "after").Y);
    }

    [Fact]
    public void Wrap_WithoutMeasurer_Throws()
    {
        var def = Define("""{ "x": 0, "y": 0, "width": 20, "height": 5, "wrap": true, "text": "aaaa" }""");

        Assert.Throws<ReportException>(() => Render(def));
    }

    [Fact]
    public void Wrap_BandGrowingBeyondPageBody_Throws()
    {
        // Six words, one per line (8 mm each in a 10 mm box) = 30 mm, but the page body is 20 mm.
        var def = Define(
            """{ "x": 0, "y": 0, "width": 10, "height": 5, "wrap": true, "text": "aaaa bbbb cccc dddd eeee ffff" }""",
            pageHeight: 20);

        Assert.Throws<ReportException>(() => Render(def, new FakeTextMeasurer()));
    }
}
