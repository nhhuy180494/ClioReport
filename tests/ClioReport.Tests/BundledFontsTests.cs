using ClioReport.Pdf;
using Xunit;

namespace ClioReport.Tests;

public class BundledFontsTests
{
    [Theory]
    [InlineData("Arial")]
    [InlineData("Times New Roman")]
    [InlineData("Courier New")]
    [InlineData("liberation sans")]
    public void UseBundledFonts_registers_families_with_all_four_styles(string family)
    {
        var options = new PdfRenderOptions().UseBundledFonts();

        var fonts = options.Fonts[family];
        foreach (var source in new[] { fonts.Regular, fonts.Bold, fonts.Italic, fonts.BoldItalic })
        {
            var bytes = source!.Read();
            Assert.True(bytes.Length > 100_000);
            Assert.Equal(new byte[] { 0, 1, 0, 0 }, bytes[..4]);   // TrueType header
        }
    }

    [Fact]
    public void Bundled_sans_covers_vietnamese()
    {
        var bytes = new PdfRenderOptions().UseBundledFonts().Fonts["Arial"].Regular.Read();

        var coverage = TrueTypeCoverage.Parse(bytes);

        Assert.NotNull(coverage);
        Assert.True(coverage!.Has('ế'));
        Assert.True(coverage.Has('Ư'));
    }

    [Fact]
    public void UseBundledFonts_sets_a_korean_fallback_unless_one_is_given()
    {
        var options = new PdfRenderOptions().UseBundledFonts();

        var coverage = TrueTypeCoverage.Parse(options.FallbackFont!.Read());
        Assert.NotNull(coverage);
        Assert.True(coverage!.Has('한'));
        Assert.NotNull(options.FallbackBoldFont);

        var own = new PdfRenderOptions { FallbackFontFilePath = "mine.ttf" }.UseBundledFonts();
        Assert.Null(own.FallbackFont);
    }
}
