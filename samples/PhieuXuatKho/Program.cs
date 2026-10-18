using ClioReport;
using ClioReport.Pdf;

var json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "phieu-xuat-kho.json"));
var definition = ReportSerializer.Deserialize(json);

var report = ReportEngine.Render(definition, SampleContext.Create());

var output = Path.Combine(Environment.CurrentDirectory, "phieu-xuat-kho.pdf");
PdfReportRenderer.Render(report, output);

Console.WriteLine($"{report.Pages.Count} page(s) -> {output}");
