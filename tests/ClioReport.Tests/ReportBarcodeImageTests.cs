using ClioReport.Model;
using Xunit;

namespace ClioReport.Tests;

public class ReportBarcodeImageTests
{
    private static ReportDefinition Def(string elementJson) => ReportSerializer.Deserialize($$"""
    {
      "version": 1,
      "bands": [ { "type": "reportHeader", "height": 10, "elements": [ {{elementJson}} ] } ]
    }
    """);

    private static RenderedElement Render(string elementJson, object root) =>
        Assert.Single(Assert.Single(ReportEngine.Render(Def(elementJson), new ObjectDataContext(root)).Pages).Elements);

    private const string Barcode = """{ "type": "barcode", "width": 60, "height": 8, "expression": "code" }""";
    private const string Image = """{ "type": "image", "width": 20, "height": 10, "expression": "logo" }""";

    [Fact]
    public void Barcode_CarriesValue()
    {
        var element = Render(Barcode, new { Code = "PXK-1" });

        Assert.Equal(ElementKind.Barcode, element.Kind);
        Assert.Equal("PXK-1", element.Text);
    }

    [Fact]
    public void Barcode_EmptyValue_IsAllowed() =>
        Assert.Equal("", Render(Barcode, new { Code = "" }).Text);

    [Fact]
    public void Barcode_UnencodableValue_Throws() =>
        Assert.Throws<ReportException>(() => Render(Barcode, new { Code = "Tiếng Việt" }));

    [Fact]
    public void Image_ByteArrayValue_IsPassedThrough()
    {
        var bytes = new byte[] { 1, 2, 3 };

        var element = Render(Image, new { Logo = bytes });

        Assert.Equal(ElementKind.Image, element.Kind);
        Assert.Same(bytes, element.Image);
        Assert.Equal("", element.Text);
    }

    [Fact]
    public void Image_StringValue_IsAPath()
    {
        var element = Render(Image, new { Logo = "logo.png" });

        Assert.Null(element.Image);
        Assert.Equal("logo.png", element.Text);
    }

    [Fact]
    public void Image_StaticText_IsAPath()
    {
        var element = Render("""{ "type": "image", "width": 20, "height": 10, "text": "logo.png" }""", new { });

        Assert.Equal("logo.png", element.Text);
    }
}
