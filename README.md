# ClioReport

Band-based report builder library for .NET 8 (MIT). Define a report as versioned JSON (bands, groups, aggregates, expressions), feed it any data source, get positioned pages, render them to PDF.

> Status: phase 1, engine only (no designer/UI). Not published to NuGet yet; the API may change.

## Quick start

`report.json`:

```json
{
  "version": 1,
  "dataSource": "items",
  "groups": [ { "name": "kho", "expression": "kho" } ],
  "bands": [
    { "type": "groupHeader", "group": "kho", "height": 7, "elements": [
      { "width": 190, "height": 6, "expression": "'Kho: ' + kho" } ] },
    { "type": "detail", "height": 6, "elements": [
      { "width": 40, "height": 5, "expression": "code" },
      { "x": 140, "width": 25, "height": 5, "align": "right", "expression": "Format(qty, 'N0')" } ] },
    { "type": "groupFooter", "group": "kho", "height": 8, "elements": [
      { "width": 190, "height": 6, "align": "right", "expression": "'Tong: ' + Format(tongKho, 'N0')" } ] }
  ],
  "aggregates": [ { "name": "tongKho", "function": "sum", "expression": "qty", "group": "kho" } ]
}
```

```csharp
using ClioReport;
using ClioReport.Pdf;

var definition = ReportSerializer.Deserialize(File.ReadAllText("report.json"));

using var measurer = new PdfTextMeasurer();   // only needed when an element uses "wrap": true
var report = ReportEngine.Render(definition, data, measurer);

PdfReportRenderer.Render(report, "report.pdf");
```

`data` is any implementation of `IDataContext`:

```csharp
public interface IDataContext
{
    bool TryGetValue(string name, out object? value);         // a field of this row / of the root
    IEnumerable<IDataContext> GetRows(string source);          // rows of a collection, e.g. "items"
}
```

Rows must already be sorted by the group expressions. See `samples/PhieuXuatKho` for a complete example and `docs/README.md` for the conventions (units, field lookup, aggregates, pagination).

### Fonts

The PDF package uses PDFsharp Core, which loads no fonts by itself. By default `arial.ttf` from the Windows fonts folder is used (it covers Vietnamese). On other platforms, or for another font, set `PdfRenderOptions.FontFilePath` (pass the same options to `PdfTextMeasurer` and `PdfReportRenderer`), or assign `GlobalFontSettings.FontResolver` yourself.

## Projects

- `src/ClioReport.Core`: JSON model, expression sandbox, layout engine, `IDataContext`. No dependencies.
- `src/ClioReport.Pdf`: PDF renderer (PDFsharp), kept separate so Core stays dependency-free.
- `tests/ClioReport.Tests`, `samples/PhieuXuatKho`, `docs`.

## Build

```
dotnet build
dotnet test
dotnet run --project samples/PhieuXuatKho
```

## License

MIT, see [LICENSE](LICENSE).
