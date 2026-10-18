# Changelog

## Unreleased

- `page.columns` and `page.columnGap`: multi-column detail layout for labels.

## 0.1.0-alpha.2

- Band `columnHeader`: printed after the report header and again on every new page.
- Elements: `color`, `fill` and `border` for text, line and rect elements; `padding` for text.

## 0.1.0-alpha.1

First public preview.

- `ClioReport.Core`: versioned JSON report model, bands (report/page/group/detail/footers), groups, aggregates, sandboxed expressions, page layout with keep-together and `pageBreakBefore`, text wrapping, `IDataContext` and `ObjectDataContext`.
- Elements: text (`bold`, `italic`, `fontSize`, `wrap`), line, rect, Code 128 `barcode`, `qrcode`, `image`. Band `visible` expression.
- `ClioReport.Pdf`: PDF renderer on PDFsharp, text measurer, optional bold/italic font files, image loading restricted to `ImageRoot`.
