# Docs

## Engine conventions

- Units are millimetres. Element `x`/`y` are relative to their band; `RenderedElement` positions are absolute on the page.
- Detail rows come from `IDataContext.GetRows(definition.DataSource)` and must be **pre-sorted** by the group expressions (outer group first). The engine does not sort.
- Field lookup order in expressions: variables (aggregates, `PageNumber`, `PageCount`) -> current row -> root context. Names are case-sensitive.
- Aggregates: `Count` counts non-null values, the rest need numbers, nulls are skipped. `group` = reset when that group starts; omitted = whole report.
- Page header/footer bands are evaluated after layout: they can use `PageNumber`, `PageCount`, root fields and report-wide aggregates, but not row fields.
- Bands are never split across pages. A group header (all its header bands) is kept with the first row's detail bands: if they do not fit in the space left, both move to the next page. Set `pageBreakBefore: true` on a group to start each of its instances on a new page (the first one never leaves an empty page).
- Element `align`: `left` (default), `center`, `right`, inside the element box (`x`..`x+width`).
- Element `type`: `text` (default), `line` (from `x,y` to `x+width,y+height`; use `height: 0` for a horizontal rule) or `rect` (outline). `thickness` is in mm (default 0.2).
- Numbers print with invariant culture; use `Format(x, 'N0')` for thousands separators.
