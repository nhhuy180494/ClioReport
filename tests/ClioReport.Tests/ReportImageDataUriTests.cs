using ClioReport.Model;
using Xunit;

namespace ClioReport.Tests;

public class ReportImageDataUriTests
{
    [Fact]
    public void Image_ExpressionWithDataUri_IsTheImage()
    {
        var definition = new ReportDefinition
        {
            Version = 1,
            Bands =
            {
                new BandDefinition
                {
                    Type = BandType.ReportHeader,
                    Height = 20,
                    Elements = { new ElementDefinition { Type = "image", Width = 30, Height = 20, Expression = "'data:image/png;base64,iVBORw0KGgo='" } }
                }
            }
        };

        var report = ReportEngine.Render(definition, new DictionaryDataContext());

        var image = Assert.Single(report.Pages[0].Elements, e => e.Kind == ElementKind.Image);
        Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, image.Image);
    }
}
