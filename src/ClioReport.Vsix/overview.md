# ClioReport Designer

Design reports for the open-source [ClioReport](https://github.com/nhhuy180494/ClioReport) .NET library without leaving Visual Studio.

ClioReport is a band-based report builder for .NET 8: a report is a JSON document (bands, groups, aggregates, expressions) that you render to PDF from your own data. This extension adds a visual designer for those report files.

![ClioReport Designer](https://raw.githubusercontent.com/nhhuy180494/ClioReport/main/docs/images/designer.png)

## What you get

- **Visual designer** for `.clio` report files: canvas with rulers, structure outline, properties panel, drag elements onto bands, multi-select, align and distribute, smart guides while dragging (snap to neighbours and equal gaps), Ctrl+wheel zoom, Space+drag pan, undo and redo.
- **Open from Solution Explorer**: double-click a `.clio` file (works in Folder View too), or right-click and choose *Open in ClioReport Designer*. Each file opens in its own tab and `Ctrl+S` saves it.
- **Add New Item > ClioReport Report** creates a blank report in C# projects.
- **Font per element** and image elements that take a file path or bytes.
- **Workspace**: hide or resize the side panels, focus mode, built-in help (`?` or `F1`) with expression syntax and shortcuts.
- **Table wizard**: pick a collection and its fields from a sample data file and get a table in one step.
- **PDF preview** with your own sample data (a JSON file).
- **RDLC import**: convert an existing `.rdlc` report; anything that cannot be converted is listed, not guessed.

## Use a report in your code

```csharp
var definition = ReportSerializer.Deserialize(File.ReadAllText("report.clio"));
// render it with ReportEngine and the ClioReport.Pdf renderer, see the project README
```

Packages: `ClioReport.Core`, `ClioReport.Pdf`, `ClioReport.Rdlc` and `ClioReport.Pdf.Fonts` (bundled fonts for Linux and Docker) on NuGet (prerelease).

## Requirements

- Visual Studio 2022 or 2026 (x64)
- .NET 8 runtime (the designer runs as a small local process)
- WebView2 runtime (included with current Windows and Microsoft Edge)

## Privacy

The designer runs entirely on your machine. It starts a local web server bound to `127.0.0.1` on a free port for the editor window and sends no data over the network. No telemetry is collected.

## Status

Early release (0.1.x). Feedback and bug reports are welcome on [GitHub](https://github.com/nhhuy180494/ClioReport/issues). Licensed under MIT.
