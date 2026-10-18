# Changelog

## Unreleased

## 0.1.0-alpha.5 (2026-10-18)

- `src/ClioReport.Vsix`: Visual Studio extension (2022 and newer; built with `build-vsix.ps1`, not part of `ClioReport.sln`; the `.vsix` is attached to the GitHub release). Report files use the `.clio` extension (JSON inside; `.json` still opens in the web designer).
  - Double-click a `.clio` file in Solution Explorer (Solution view or Folder View) to open it in the designer; right-click > *Open in ClioReport Designer*; *Tools > ClioReport Designer* opens an empty report.
  - *Add New Item > ClioReport Report* creates a blank `.clio` (C# projects).
  - `Ctrl+S` writes the file; PDF preview and RDLC import work inside Visual Studio (WebView2, the designer host runs as a child process).
- Designer: reports are saved as `.clio` by default.
- RDLC import: `Image` items keep their position and size as `image` elements. A database image is bound to its field; an embedded image is bound to a field named after it (supply the picture as `byte[]`), with a warning, because images are data and are not stored in the report. Column groups (dynamic columns) are still not imported and are reported.

## 0.1.0-alpha.4 (2026-10-18)

- Text elements: `underline` and `rotate` (0, 90, 270).
- New element types `table` (columns, fixed rows or a collection, header, zebra rows, wrapping rows) and `list` (repeats an item template per item of a collection, `across`, `gap`). Both expand into plain text elements, so renderers need no change.
- Fix: an element with width 0 (a vertical line) was rejected by the padding check.
- `src/ClioReport.Designer`: web designer for report JSON (canvas, outline, ruler, properties, undo/redo, drag elements from the toolbar, multi-select with align/distribute/shared style).
- Designer *Import RDLC* button (`POST /api/import-rdlc` on the designer host).
- RDLC import: column-hierarchy header cells (spanning headers) become the `columnHeader` band, `IIf` is translated, `First/Last(...)` become plain fields, paragraphs keep their line breaks and paragraphs with different styles become separate elements.
- Fixed RDLC import ignoring `<Page>` (size, margins, page header/footer) when it sits inside `<ReportSection>`, which is where Visual Studio writes it.
- Designer PDF preview: `dotnet run --project src/ClioReport.Designer` serves the designer and renders `POST /api/preview` with ClioReport.Pdf; sample data is a JSON file (see `samples/GoodsReceipt/goods-receipt.data.json`).
- New package `ClioReport.Rdlc`: `RdlcImporter` converts an `.rdlc`/`.rdl` report (Tablix, row groups, page header/footer, text boxes, lines) into a `ReportDefinition` and lists what it skipped.

## 0.1.0-alpha.3

- `page.columns` and `page.columnGap`: multi-column detail layout for labels.
- Elements: `vAlign` and `fitBand` for table cells.
- `ClioReport.Pdf`: font fallback (`FallbackFontFilePath`) for text that mixes scripts, e.g. Vietnamese and Korean.

## 0.1.0-alpha.2

- Band `columnHeader`: printed after the report header and again on every new page.
- Elements: `color`, `fill` and `border` for text, line and rect elements; `padding` for text.

## 0.1.0-alpha.1

First public preview.

- `ClioReport.Core`: versioned JSON report model, bands (report/page/group/detail/footers), groups, aggregates, sandboxed expressions, page layout with keep-together and `pageBreakBefore`, text wrapping, `IDataContext` and `ObjectDataContext`.
- Elements: text (`bold`, `italic`, `fontSize`, `wrap`), line, rect, Code 128 `barcode`, `qrcode`, `image`. Band `visible` expression.
- `ClioReport.Pdf`: PDF renderer on PDFsharp, text measurer, optional bold/italic font files, image loading restricted to `ImageRoot`.
