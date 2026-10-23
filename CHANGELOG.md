# Changelog

## Unreleased

- RDLC import: `First(Fields!X.Value, ...)` of a plain field now becomes `X` instead of `(X)`.

## 0.1.0-alpha.8 (2026-10-23)

- Designer: the fields of the data are saved in the report (optional `schema`: field names and types per collection). Paste or load a JSON example once (*Report > Data fields > Define from JSON…*, or by loading a sample); the *Data fields* lists and the Table wizard then work without loading any file. The engine ignores `schema`.

- Table total row: a column can set `total` (`sum`, `count`, `avg`, `min`, `max`), plus `totalFormat` and the table's `totalLabel`; the table ends with a bold total row. Designer: *Total* and *Total format* per column, *Total label* in Table style. Needs a table with a `source`.

- Designer: *Data fields* section in the properties of text, barcode and QR elements. It lists the row fields of the data source, the report fields and the variables (PageNumber, PageCount, aggregates) from the loaded sample data, with their types; a click inserts the name into the Expression box at the caret.

## 0.1.0-alpha.7 (2026-10-20)

- Image elements: a path with a leading `/` or `\` (such as `/systemConfig/logo.png`) is now relative to `PdfRenderOptions.ImageRoot` instead of failing.
- Designer workspace: the Structure and Properties panels can be hidden (`[` and `]`, or the toolbar buttons) and resized by dragging their edge (double-click resets); *Focus mode* (`\` or Esc to leave) shows only the canvas; a button next to the zoom shows only the area with bands instead of the whole blank page; Properties sections fold (remembered) and fields flow in two columns when the panel is wide. The zoom re-fits the width when the layout changes. Layout is remembered per browser.
- Designer: zoom and pan like a design tool. `Ctrl` + wheel (or trackpad pinch) zooms around the pointer; hold `Space` and drag, or drag with the middle button, to pan; `Ctrl` + `0` fits the width, `Ctrl` + `+`/`-` zoom in steps.
- Designer: help dialog (`?` button or `F1`) with the workflow and shortcuts; new guide `docs/designer.md`.

## 0.1.0-alpha.6 (2026-10-19)

- Designer: *Table wizard* (wand button in the left rail, or *Table wizard…* in a table's properties). Pick a collection and its fields from the loaded sample data (or add columns by hand), set header, alignment, width and format per column, then insert a new table or apply the result to the selected one.
- Per-element font: text and table elements take an optional `fontFamily`. Register families with `PdfRenderOptions.Fonts` (name -> regular/bold/italic/bold-italic TrueType files); an element without `fontFamily`, or with an unknown name, uses `FontFilePath`. `TextStyle` gained a `FontFamily` member, so measuring and wrapping use the same font. Designer: *Font* dropdown (the host offers the families found in the OS fonts folder) and an *Image folder* box in the Preview window, so image elements whose value is a file path load from that folder (`PdfRenderOptions.ImageRoot`).
- Designer: smart guides while dragging or resizing an element. It snaps to the edges and centres of neighbouring elements and the band (pink lines), and to equal gaps between neighbours (pink gap labels in mm). Alt turns it off. An image element has one *Image* box that takes a file path or a field name, and an image with no picture is drawn as a dashed "No image" box instead of failing.
- New package `ClioReport.Pdf.Fonts`: Liberation Sans, Serif and Mono plus Nanum Gothic for Hangul (SIL OFL 1.1, about 14 MB) embedded in the assembly. `new PdfRenderOptions().UseBundledFonts()` registers them as "Arial", "Times New Roman" and "Courier New" (and as "Liberation ..."), so PDFs look the same on Windows, Linux and Docker without installing fonts. Fonts can now also be supplied from memory (`FontSource.FromBytes`); `PdfRenderOptions.FontFamily` picks the default font from `Fonts` when `FontFilePath` is not set. `UseBundledFonts()` also sets Nanum Gothic as the fallback for Korean text unless you set `FallbackFont`/`FallbackFontFilePath`; `PdfRenderOptions.FallbackFont` and `FallbackBoldFont` accept any `FontSource`.

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
