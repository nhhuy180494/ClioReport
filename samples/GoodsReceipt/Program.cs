using ClioReport;
using ClioReport.Pdf;

// Arial covers Vietnamese, Malgun Gothic covers Hangul: Arial is the main font and Malgun the fallback for the
// characters Arial lacks. Without Malgun the Korean text is not drawn correctly.
var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
string? Font(string file) => File.Exists(Path.Combine(fonts, file)) ? Path.Combine(fonts, file) : null;

var options = new PdfRenderOptions
{
    FontSize = 8,
    FontFilePath = Path.Combine(fonts, "arial.ttf"),
    BoldFontFilePath = Font("arialbd.ttf"),
    ItalicFontFilePath = Font("ariali.ttf"),
    BoldItalicFontFilePath = Font("arialbi.ttf"),
    FallbackFontFilePath = Font("malgun.ttf"),
    FallbackBoldFontFilePath = Font("malgunbd.ttf")
};

var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "goods-receipt.json"));
var definition = ReportSerializer.Deserialize(json);

using var measurer = new PdfTextMeasurer(options);
var report = ReportEngine.Render(definition, SampleData.Create(), measurer);

var output = Path.Combine(Environment.CurrentDirectory, "goods-receipt.pdf");
PdfReportRenderer.Render(report, output, options);

Console.WriteLine($"{report.Pages.Count} page(s) -> {output}");
