# Docs

## Engine conventions

- Units are millimetres. Element `x`/`y` are relative to their band; `RenderedElement` positions are absolute on the page.
- Detail rows come from `IDataContext.GetRows(definition.DataSource)` and must be **pre-sorted** by the group expressions (outer group first). The engine does not sort.
- `ObjectDataContext` reads public properties, dictionary keys and `DataRow` columns (case-insensitive, dotted paths allowed, DBNull = null); it never calls methods. Row collections come from its `sources` or from a collection property of the root object.
- Field lookup order in expressions: variables (aggregates, `PageNumber`, `PageCount`) -> current row -> root context. Names are case-sensitive.
- Aggregates: `Count` counts non-null values, the rest need numbers, nulls are skipped. `group` = reset when that group starts; omitted = whole report.
- Page header/footer bands are evaluated after layout: they can use `PageNumber`, `PageCount`, root fields and report-wide aggregates, but not row fields.
- Bands are never split across pages. A group header (all its header bands) is kept with the first row's detail bands: if they do not fit in the space left, both move to the next page. Set `pageBreakBefore: true` on a group to start each of its instances on a new page (the first one never leaves an empty page).
- Element `align`: `left` (default), `center`, `right`, inside the element box (`x`..`x+width`).
- Element `type`: `text` (default), `line` (from `x,y` to `x+width,y+height`; use `height: 0` for a horizontal rule) or `rect` (outline). `thickness` is in mm (default 0.2).
- `wrap: true` on a text element wraps it to the element `width` (word wrap, long words break by character, `\n` is a paragraph break) and grows the band to fit. It needs an `ITextMeasurer`: pass `new PdfTextMeasurer(options)` (same `PdfRenderOptions` as the renderer) to `ReportEngine.Render`. Page header/footer bands never grow, and the keep-together check uses defined band heights, not grown ones.
- `type: "qrcode"`: QR code of the element's expression/text (UTF-8, error correction level M, version picked automatically; text too long for version 40 is a `ReportException`). Drawn as a square of side `min(width, height)`, positioned by `align`, with square modules. It does not add a quiet zone: leave at least 4 modules of blank space around it, and print at 20 mm or more for a short text.
- `type: "barcode"`: Code 128 of the element's expression/text, drawn as bars scaled to the element `width` (set C for digit-only text of 4+ even digits, set B for printable ASCII; anything else is a `ReportException`). Leave a quiet zone (about 10 modules of blank space) around it and keep the width at or above ~0.25 mm per module (`Code128.Encode(text).Length` gives the module count). It does not print the human-readable text: add a normal text element for that.
- `type: "image"`: the expression's value is the image. A `byte[]` (PNG, JPEG or BMP) is used directly; a string is a file path, which the PDF renderer only loads when `PdfRenderOptions.ImageRoot` is set and the path stays inside that folder. The image keeps its aspect ratio, fits inside the element box and follows `align`.
- Numbers print with invariant culture; use `Format(x, 'N0')` for thousands separators.
