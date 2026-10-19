using System.Text.Json;
using ClioReport;
using System.Xml;
using ClioReport.Pdf;
using ClioReport.Rdlc;

// Local-only host for the web designer: serves wwwroot and renders a PDF preview of the report being edited.
//   dotnet run --project src/ClioReport.Designer            -> http://localhost:5178
//   dotnet run --project src/ClioReport.Designer -- --urls http://localhost:6000
var builder = WebApplication.CreateBuilder(args);
if (builder.Configuration["urls"] is null) builder.WebHost.UseUrls("http://localhost:5178");
var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    // The designer is a single file that changes often: never serve a stale copy.
    OnPrepareResponse = ctx => ctx.Context.Response.Headers.CacheControl = "no-cache"
});

var options = CreateOptions();

// Font families the designer offers per element (the ones found in the OS fonts folder).
app.MapGet("/api/fonts", () => options.Fonts.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase));

// Body: { "report": <report JSON>, "data": <any JSON object, optional> }. Returns application/pdf, or 400 + text.
app.MapPost("/api/preview", async (HttpRequest request) =>
{
    try
    {
        using var doc = await JsonDocument.ParseAsync(request.Body);
        if (!doc.RootElement.TryGetProperty("report", out var reportJson))
            return Results.Text("Missing 'report'.", statusCode: 400);

        var definition = ReportSerializer.Deserialize(reportJson.GetRawText());
        // With sample data the engine stays strict (a misspelt field is reported). Without any, every field is blank so the
        // layout can still be previewed.
        IDataContext data = doc.RootElement.TryGetProperty("data", out var dataJson) && dataJson.ValueKind == JsonValueKind.Object
            ? new ObjectDataContext((Dictionary<string, object?>)Convert(dataJson)!)
            : new BlankData();

        // Image elements whose value is a file path load from this folder (typed in the designer; local host only).
        var imageRoot = doc.RootElement.TryGetProperty("imageRoot", out var rootJson) && rootJson.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(rootJson.GetString()) ? rootJson.GetString() : null;
        if (imageRoot is not null && !Directory.Exists(imageRoot))
            return Results.Text($"Image folder not found: {imageRoot}", statusCode: 400);
        var renderOptions = CreateOptions(imageRoot);

        using var measurer = new PdfTextMeasurer(renderOptions);
        var report = ReportEngine.Render(definition, data, measurer);

        using var ms = new MemoryStream();
        PdfReportRenderer.Render(report, ms, renderOptions);
        return Results.File(ms.ToArray(), "application/pdf");
    }
    catch (Exception ex) when (ex is ReportException or JsonException or IOException or InvalidOperationException)
    {
        return Results.Text(ex.Message, statusCode: 400);
    }
});

// Body: the raw .rdlc/.rdl XML. Returns { report: "<report JSON>", warnings: [...] }, or 400 + text.
app.MapPost("/api/import-rdlc", async (HttpRequest request) =>
{
    try
    {
        using var ms = new MemoryStream(); // Kestrel forbids synchronous reads of the request body
        await request.Body.CopyToAsync(ms); ms.Position = 0;
        var result = RdlcImporter.Import(ms);
        return Results.Json(new { report = ReportSerializer.Serialize(result.Definition), warnings = result.Warnings });
    }
    catch (Exception ex) when (ex is ReportException or XmlException)
    {
        return Results.Text(ex.Message, statusCode: 400);
    }
});

app.Lifetime.ApplicationStarted.Register(() => Console.WriteLine("ClioReport designer: " + string.Join(", ", app.Urls)));
app.Run();

// JSON -> plain objects ObjectDataContext understands. A string "data:image/...;base64,..." becomes byte[] so image
// elements can be previewed without files on disk.
static object? Convert(JsonElement e) => e.ValueKind switch
{
    JsonValueKind.Object => e.EnumerateObject().ToDictionary(p => p.Name, p => Convert(p.Value), StringComparer.OrdinalIgnoreCase),
    JsonValueKind.Array => e.EnumerateArray().Select(Convert).ToList(),
    JsonValueKind.String => FromString(e.GetString()!),
    JsonValueKind.Number => e.TryGetInt64(out var l) ? (l is >= int.MinValue and <= int.MaxValue ? (object)(int)l : l) : e.GetDecimal(),
    JsonValueKind.True => true,
    JsonValueKind.False => false,
    _ => null
};

static object FromString(string s)
{
    const string marker = ";base64,";
    if (s.StartsWith("data:image/", StringComparison.Ordinal) && s.IndexOf(marker, StringComparison.Ordinal) is var i and > 0)
    {
        try { return System.Convert.FromBase64String(s[(i + marker.Length)..]); } catch (FormatException) { }
    }
    return s;
}

// Same font setup as the samples: Arial (Vietnamese) with Malgun Gothic (Hangul) as fallback, taken from the OS fonts
// folder when present. Missing files are skipped, so the host still starts on machines without them.
static PdfRenderOptions CreateOptions(string? imageRoot = null)
{
    var fonts = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
    string? Font(string? file) => file is not null && !string.IsNullOrEmpty(fonts) && File.Exists(Path.Combine(fonts, file)) ? Path.Combine(fonts, file) : null;
    var result = new PdfRenderOptions
    {
        ImageRoot = imageRoot,
        FontSize = 8,
        FontFilePath = Font("arial.ttf"),
        BoldFontFilePath = Font("arialbd.ttf"),
        ItalicFontFilePath = Font("ariali.ttf"),
        BoldItalicFontFilePath = Font("arialbi.ttf"),
        FallbackFontFilePath = Font("malgun.ttf"),
        FallbackBoldFontFilePath = Font("malgunbd.ttf")
    };

    // Other families an element can pick. Only families whose regular file exists are offered.
    void Family(string name, string regular, string? bold, string? italic, string? boldItalic)
    {
        if (Font(regular) is not { } r) return;
        result.Fonts[name] = new PdfFontFiles(r) { Bold = Font(bold), Italic = Font(italic), BoldItalic = Font(boldItalic) };
    }
    Family("Arial", "arial.ttf", "arialbd.ttf", "ariali.ttf", "arialbi.ttf");
    Family("Times New Roman", "times.ttf", "timesbd.ttf", "timesi.ttf", "timesbi.ttf");
    Family("Courier New", "cour.ttf", "courbd.ttf", "couri.ttf", "courbi.ttf");
    Family("Tahoma", "tahoma.ttf", "tahomabd.ttf", null, null);
    Family("Verdana", "verdana.ttf", "verdanab.ttf", "verdanai.ttf", "verdanaz.ttf");
    Family("Segoe UI", "segoeui.ttf", "segoeuib.ttf", "segoeuii.ttf", "segoeuiz.ttf");
    Family("Calibri", "calibri.ttf", "calibrib.ttf", "calibrii.ttf", "calibriz.ttf");
    Family("Georgia", "georgia.ttf", "georgiab.ttf", "georgiai.ttf", "georgiaz.ttf");
    return result;
}

// Preview without sample data: every field is blank, there are no rows.
sealed class BlankData : IDataContext
{
    public bool TryGetValue(string name, out object? value) { value = null; return true; }
    public IEnumerable<IDataContext> GetRows(string source) => Enumerable.Empty<IDataContext>();
}
