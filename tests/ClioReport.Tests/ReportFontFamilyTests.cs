using Xunit;

namespace ClioReport.Tests;

public class ReportFontFamilyTests
{
    private static RenderedPage Render(string elementJson) => Assert.Single(ReportEngine.Render(ReportSerializer.Deserialize($$"""
    {
      "version": 1,
      "page": { "width": 200, "height": 100, "margin": 0 },
      "bands": [ { "type": "reportHeader", "height": 10, "elements": [ {{elementJson}} ] } ]
    }
    """), new DictionaryDataContext()).Pages);

    [Fact]
    public void Text_element_carries_its_font_family_into_the_rendered_style()
    {
        var page = Render("""{ "x": 0, "y": 0, "width": 40, "height": 5, "fontFamily": "Times New Roman", "bold": true, "text": "hi" }""");

        Assert.Equal("Times New Roman", page.Elements.Single().Style.FontFamily);
        Assert.True(page.Elements.Single().Style.Bold);
    }

    [Fact]
    public void Without_font_family_the_style_has_none()
    {
        var page = Render("""{ "x": 0, "y": 0, "width": 40, "height": 5, "text": "hi" }""");

        Assert.Null(page.Elements.Single().Style.FontFamily);
    }

    [Fact]
    public void Table_cells_use_the_table_font_family()
    {
        var page = Render("""
            { "type": "table", "x": 0, "y": 0, "width": 60, "height": 5, "fontFamily": "Courier New",
              "columns": [ { "header": "A", "width": 30 } ], "rows": [ [ "x" ] ] }
            """);

        Assert.All(page.Elements.Where(e => e.Kind == ElementKind.Text), e => Assert.Equal("Courier New", e.Style.FontFamily));
    }

    [Fact]
    public void Font_family_survives_a_serialize_round_trip()
    {
        var json = ReportSerializer.Serialize(ReportSerializer.Deserialize("""
            { "version": 1, "bands": [ { "type": "detail", "height": 5, "elements": [
              { "width": 10, "height": 5, "fontFamily": "Georgia", "text": "x" } ] } ] }
            """));

        Assert.Contains("\"fontFamily\": \"Georgia\"", json);
    }
}
