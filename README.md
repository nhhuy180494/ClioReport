# ClioReport

[![CI](https://github.com/nhhuy180494/ClioReport/actions/workflows/ci.yml/badge.svg)](https://github.com/nhhuy180494/ClioReport/actions/workflows/ci.yml)

Band-based report builder library for .NET 8 (MIT). Define a report as versioned JSON (bands, groups, aggregates, expressions), feed it any data source, get positioned pages, render them to PDF.

> Status: alpha (0.1.0-alpha.x), published on NuGet as prerelease packages; the API may change.

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

You rarely need to implement it: `ObjectDataContext` wraps plain objects, dictionaries and `DataTable`/`DataView`:

```csharp
var data = new ObjectDataContext(
    new { Title = "PHIEU XUAT KHO" },                          // root fields (properties, case-insensitive)
    new Dictionary<string, object> { ["items"] = items });      // rows: List<T>, DataTable, dictionaries, ...
```

Rows must already be sorted by the group expressions. See `samples/PhieuXuatKho` for a complete example and `docs/README.md` for the conventions (units, field lookup, aggregates, pagination).

### Fonts

The PDF package uses PDFsharp Core, which loads no fonts by itself. By default `arial.ttf` from the Windows fonts folder is used (it covers Vietnamese). On other platforms, or for another font, set `PdfRenderOptions.FontFilePath` (pass the same options to `PdfTextMeasurer` and `PdfReportRenderer`), or assign `GlobalFontSettings.FontResolver` yourself.

### Linux and Docker

The PDF renderer is managed code (PDFsharp Core): it needs no `libgdiplus` and no fonts installed in the image. Every push is built and tested on Ubuntu and Windows (see the CI badge). On Linux:

- Add the `ClioReport.Pdf.Fonts` package and call `options.UseBundledFonts()` on the `PdfRenderOptions` you pass to `PdfTextMeasurer` and `PdfReportRenderer`. It registers Liberation Sans/Serif/Mono as Arial/Times New Roman/Courier New and Nanum Gothic for Hangul.
- Set `PdfRenderOptions.ImageRoot` to the folder your image paths are relative to (for example `Path.Combine(env.ContentRootPath, "wwwroot")`); paths are case-sensitive on Linux and cannot leave that folder. A missing file prints a *No image* box.
- Use `/` in image paths and file names; `\` is an escape character inside report expressions.

## Projects

- `src/ClioReport.Core`: JSON model, expression sandbox, layout engine, `IDataContext`. No dependencies.
- `src/ClioReport.Pdf`: PDF renderer (PDFsharp), kept separate so Core stays dependency-free.
- `src/ClioReport.Pdf.Fonts`: optional embedded fonts (Liberation, SIL OFL) for Linux/Docker: `options.UseBundledFonts()`.
- `src/ClioReport.Xlsx`: Excel (.xlsx) export of the laid-out report, no dependencies: `XlsxReportRenderer.Render(report, "report.xlsx")`. A grid of the text, meant for further work in Excel, not a copy of the page.
- `src/ClioReport.Designer`: web designer for the JSON (see [docs/designer.md](docs/designer.md)). `dotnet run --project src/ClioReport.Designer` and open http://localhost:5178 for the editor with PDF preview (or open `wwwroot/index.html` directly to edit without preview).
- `src/ClioReport.Vsix`: Visual Studio extension hosting the designer (2022 and newer; see its README). To install, download `ClioReport.Vsix.vsix` from the [latest release](https://github.com/nhhuy180494/ClioReport/releases) and double-click it (needs the .NET 8 runtime). Then double-click a `.clio` file, or use *Tools > ClioReport Designer*. To build it yourself run `src/ClioReport.Vsix/build-vsix.ps1`.
- `src/ClioReport.Rdlc`: imports `.rdlc` reports (common subset, with warnings); see `docs/README.md`.
- `tests/ClioReport.Tests`, `samples/PhieuXuatKho`, `docs`.

## Build

```
dotnet build
dotnet test
dotnet run --project samples/PhieuXuatKho
```

## License

MIT, see [LICENSE](LICENSE). The QR encoder is a port of Project Nayuki's MIT-licensed library, see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
