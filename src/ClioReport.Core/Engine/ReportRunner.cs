using ClioReport.Expressions;
using ClioReport.Model;

namespace ClioReport;

internal sealed class ReportRunner
{
    private const double Epsilon = 1e-6;
    private const string PageNumberName = "PageNumber";
    private const string PageCountName = "PageCount";

    private readonly ReportDefinition _def;
    private readonly IDataContext _root;
    private readonly ITextMeasurer? _measurer;
    private readonly Dictionary<string, Expression> _expressions = new();
    private readonly Dictionary<string, object?> _vars = new(StringComparer.Ordinal);
    private readonly List<AggregateState> _aggregates = new();
    private readonly List<RenderedPage> _pages = new();

    private ILookup<(BandType, string), BandDefinition> _bands = null!;
    private List<BandDefinition> _pageHeaders = new();
    private List<BandDefinition> _columnHeaders = new();
    private double _columnHeaderHeight;
    private bool _columnHeadersOn;
    private double _contentTop;
    private int _colIndex;
    private double _rowTop;
    private double _rowHeight;
    private IDataContext _rootCtx = null!;
    private List<BandDefinition> _pageFooters = new();
    private double _footerHeight;
    private double _bodyTop;
    private double _bodyBottom;
    private double _cursor;

    private object?[]? _lastKeys;
    private IDataContext? _lastCtx;

    public ReportRunner(ReportDefinition definition, IDataContext root, ITextMeasurer? measurer)
    {
        _def = definition;
        _root = root;
        _measurer = measurer;
    }

    public RenderedReport Execute()
    {
        var p = _def.Page;
        _pageHeaders = _def.Bands.Where(b => b.Type == BandType.PageHeader).ToList();
        _pageFooters = _def.Bands.Where(b => b.Type == BandType.PageFooter).ToList();
        _columnHeaders = _def.Bands.Where(b => b.Type == BandType.ColumnHeader).ToList();
        _columnHeaderHeight = _columnHeaders.Sum(b => b.Height);
        _footerHeight = _pageFooters.Sum(b => b.Height);
        _bodyTop = p.Margin + _pageHeaders.Sum(b => b.Height);
        _bodyBottom = p.Height - p.Margin - _footerHeight;
        _bands = _def.Bands.ToLookup(Key);

        Validate();
        Prepare();

        NewPage();
        var rootCtx = new EngineContext(null, _root, _vars);
        _rootCtx = rootCtx;
        EmitBands(Bands(BandType.ReportHeader), rootCtx);

        // Column headers start after the report header on page 1 and then open every new page.
        _columnHeadersOn = true;
        if (_cursor + _columnHeaderHeight > _bodyBottom + Epsilon)
            NewPage();
        else
            EmitColumnHeaders();

        if (!string.IsNullOrWhiteSpace(_def.DataSource))
        {
            foreach (var row in _root.GetRows(_def.DataSource))
                ProcessRow(row);
        }

        if (_lastCtx is not null)
            CloseGroups(0, _lastCtx);

        EmitBands(Bands(BandType.ReportFooter), rootCtx);
        FinishPages();

        return new RenderedReport(p.Width, p.Height, _pages);
    }

    // ---- setup ----

    private static (BandType, string) Key(BandDefinition b) =>
        (b.Type, (b.Type is BandType.GroupHeader or BandType.GroupFooter) ? b.Group ?? "" : "");

    private static ElementKind? KindOf(string type) => type.ToLowerInvariant() switch
    {
        "text" => ElementKind.Text,
        "line" => ElementKind.Line,
        "rect" => ElementKind.Rect,
        "barcode" => ElementKind.Barcode,
        "qrcode" => ElementKind.QrCode,
        "image" => ElementKind.Image,
        _ => null
    };

    private IEnumerable<BandDefinition> Bands(BandType type, string group = "") => _bands[(type, group)];

    private void Validate()
    {
        var p = _def.Page;
        if (p.Width <= 0 || p.Height <= 0 || p.Margin < 0 || p.Width - 2 * p.Margin <= 0)
            throw new ReportException("Invalid page size or margin.");

        if (p.Columns < 1 || p.ColumnGap < 0)
            throw new ReportException("Page columns must be at least 1 and columnGap cannot be negative.");
        if (p.Columns > 1)
        {
            if (_def.Bands.Count(b => b.Type == BandType.Detail) != 1)
                throw new ReportException("A report with several columns needs exactly one detail band.");
            if (CellWidth <= 0)
                throw new ReportException("The column gaps leave no room for the columns.");
        }

        var bodyHeight = _bodyBottom - _bodyTop;
        if (bodyHeight <= 0)
            throw new ReportException("Page header/footer bands leave no room for the page body.");
        bodyHeight -= _columnHeaderHeight;
        if (bodyHeight <= 0)
            throw new ReportException("Column header bands leave no room for the page body.");

        var groupNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var g in _def.Groups)
        {
            if (string.IsNullOrWhiteSpace(g.Name) || string.IsNullOrWhiteSpace(g.Expression))
                throw new ReportException("Every group needs a name and an expression.");
            if (!groupNames.Add(g.Name))
                throw new ReportException($"Duplicate group '{g.Name}'.");
        }

        foreach (var b in _def.Bands)
        {
            if (b.Height < 0)
                throw new ReportException($"{b.Type} band has a negative height.");

            if (b.Type is BandType.GroupHeader or BandType.GroupFooter
                && (b.Group is null || !groupNames.Contains(b.Group)))
                throw new ReportException($"{b.Type} band references unknown group '{b.Group}'.");

            var isPageBand = b.Type is BandType.PageHeader or BandType.PageFooter;
            if (!isPageBand && b.Type != BandType.ColumnHeader && b.Height > bodyHeight + Epsilon)
                throw new ReportException(
                    $"{b.Type} band (height {b.Height}) does not fit in the page body (height {bodyHeight}).");

            foreach (var e in b.Elements)
                ValidateElement(b, e);
        }

        var aggregateNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in _def.Aggregates)
        {
            if (string.IsNullOrWhiteSpace(a.Name) || string.IsNullOrWhiteSpace(a.Expression))
                throw new ReportException("Every aggregate needs a name and an expression.");
            if (a.Name is PageNumberName or PageCountName)
                throw new ReportException($"'{a.Name}' is a reserved name.");
            if (!aggregateNames.Add(a.Name))
                throw new ReportException($"Duplicate aggregate '{a.Name}'.");
            if (a.Group is not null && !groupNames.Contains(a.Group))
                throw new ReportException($"Aggregate '{a.Name}' references unknown group '{a.Group}'.");
        }
    }

    /// <summary>Parses every expression up front so syntax errors surface before layout starts.</summary>
    private void Prepare()
    {
        foreach (var g in _def.Groups)
            Compile(g.Expression);

        foreach (var a in _def.Aggregates)
            _aggregates.Add(new AggregateState(a, Compile(a.Expression)));

        foreach (var b in _def.Bands)
        {
            if (!string.IsNullOrWhiteSpace(b.Visible))
                Compile(b.Visible);

            foreach (var e in b.Elements)
            {
                if (!string.IsNullOrWhiteSpace(e.Expression))
                    Compile(e.Expression);
            }
        }

        PublishAggregates();
    }

    private Expression Compile(string text)
    {
        if (_expressions.TryGetValue(text, out var expression)) return expression;
        try
        {
            expression = Expression.Parse(text);
        }
        catch (ExpressionException ex)
        {
            throw new ReportException($"Invalid expression '{text}': {ex.Message}", ex);
        }
        _expressions[text] = expression;
        return expression;
    }

    private object? Eval(string text, IDataContext ctx)
    {
        try
        {
            return Compile(text).Evaluate(ctx);
        }
        catch (ExpressionException ex)
        {
            throw new ReportException($"Error in expression '{text}': {ex.Message}", ex);
        }
    }

    private void ValidateElement(BandDefinition b, ElementDefinition e)
    {
        var type = e.Type.ToLowerInvariant();
        if (type is not ("table" or "list") && KindOf(e.Type) is null)
            throw new ReportException($"Unsupported element type '{e.Type}'.");
        if (e.Padding < 0 || (e.Padding > 0 && 2 * e.Padding >= e.Width))
            throw new ReportException($"{b.Type} band: padding must be at least 0 and less than half the element width.");
        if (e.Rotate is not (0 or 90 or 270))
            throw new ReportException($"{b.Type} band: rotate must be 0, 90 or 270.");
        CheckColor(b, e.Color);
        CheckColor(b, e.Fill);
        CheckColor(b, e.AltFill);
        if (e.FontSize < 0)
            throw new ReportException("An element has a negative fontSize.");
        if (e.Wrap && _measurer is null)
            throw new ReportException("An element uses 'wrap' but no ITextMeasurer was passed to ReportEngine.Render.");

        if (type == "table")
        {
            if (e.Width <= 0) throw new ReportException($"{b.Type} band: a table needs a width.");
            if (e.Columns is null || e.Columns.Count == 0)
                throw new ReportException($"{b.Type} band: a table needs at least one column.");
            if (string.IsNullOrWhiteSpace(e.Source) == (e.Rows is null))
                throw new ReportException($"{b.Type} band: a table needs either 'source' or 'rows' (not both).");
            if (e.RowHeight < 0 || e.HeaderHeight < 0)
                throw new ReportException($"{b.Type} band: table row heights cannot be negative.");
            if (e.Columns.Any(c => c.Width < 0))
                throw new ReportException($"{b.Type} band: a table column has a negative width.");
            var fixedWidth = e.Columns.Sum(c => c.Width);
            var free = e.Columns.Count(c => c.Width == 0);
            if (fixedWidth > e.Width + Epsilon || (free > 0 && fixedWidth >= e.Width - Epsilon))
                throw new ReportException($"{b.Type} band: the table columns do not fit in the table width ({e.Width} mm).");
            if (e.Padding > 0 && ColumnWidths(e).Any(w => 2 * e.Padding >= w))
                throw new ReportException($"{b.Type} band: table padding is too large for the narrowest column.");
            if (e.Rows is not null && e.Rows.Any(r => r.Count > e.Columns.Count))
                throw new ReportException($"{b.Type} band: a table row has more cells than the table has columns.");
        }
        else if (type == "list")
        {
            if (e.Width <= 0) throw new ReportException($"{b.Type} band: a list needs a width.");
            if (string.IsNullOrWhiteSpace(e.Source))
                throw new ReportException($"{b.Type} band: a list needs a 'source'.");
            if (e.Items is null || e.Items.Count == 0)
                throw new ReportException($"{b.Type} band: a list needs at least one element in 'items'.");
            if (e.Across < 1 || e.Gap < 0 || e.ItemHeight < 0)
                throw new ReportException($"{b.Type} band: a list needs across of at least 1 and no negative gap or itemHeight.");
            if (e.Gap * (e.Across - 1) >= e.Width - Epsilon)
                throw new ReportException($"{b.Type} band: the list gaps leave no room for the items.");
            foreach (var item in e.Items) ValidateElement(b, item);
        }
    }

    private static double[] ColumnWidths(ElementDefinition e)
    {
        var columns = e.Columns!;
        var free = columns.Count(c => c.Width == 0);
        var share = free == 0 ? 0 : (e.Width - columns.Sum(c => c.Width)) / free;
        return columns.Select(c => c.Width == 0 ? share : c.Width).ToArray();
    }

    // ---- rows and groups ----

    private void ProcessRow(IDataContext row)
    {
        var ctx = new EngineContext(row, _root, _vars);
        var keys = new object?[_def.Groups.Count];
        for (var i = 0; i < keys.Length; i++)
            keys[i] = Eval(_def.Groups[i].Expression, ctx);

        var changed = _lastKeys is null ? 0 : FirstChange(_lastKeys, keys);
        if (_lastCtx is not null)
            CloseGroups(changed, _lastCtx);
        BreakBeforeGroups(changed);
        OpenGroups(changed, ctx);

        foreach (var a in _aggregates)
        {
            try
            {
                a.Add(ctx);
            }
            catch (ExpressionException ex)
            {
                throw new ReportException($"Error in aggregate '{a.Definition.Name}': {ex.Message}", ex);
            }
        }
        PublishAggregates();

        EmitBands(Bands(BandType.Detail), ctx);

        _lastKeys = keys;
        _lastCtx = ctx;
    }

    /// <summary>Index of the first group whose key changed, or the group count when none did.</summary>
    private static int FirstChange(object?[] previous, object?[] current)
    {
        for (var i = 0; i < current.Length; i++)
        {
            if (!Values.Eq(previous[i], current[i])) return i;
        }
        return current.Length;
    }

    private void CloseGroups(int from, IDataContext ctx)
    {
        for (var i = _def.Groups.Count - 1; i >= from; i--)
            EmitBands(Bands(BandType.GroupFooter, _def.Groups[i].Name), ctx);
    }

    /// <summary>
    /// Starts a new page when a group about to open asks for it, or when its header bands plus the first row's
    /// detail bands would not fit in the space left (keeps a header from being stranded at the page bottom).
    /// </summary>
    private void BreakBeforeGroups(int from)
    {
        if (from < _def.Groups.Count) FlushRow();
        if (from >= _def.Groups.Count || _cursor <= _contentTop + Epsilon) return;

        var forced = _lastCtx is not null && _def.Groups.Skip(from).Any(g => g.PageBreakBefore);

        var needed = Bands(BandType.Detail).Sum(b => b.Height);
        for (var i = from; i < _def.Groups.Count; i++)
            needed += Bands(BandType.GroupHeader, _def.Groups[i].Name).Sum(b => b.Height);

        var fitsOnEmptyPage = needed <= _bodyBottom - _bodyTop - _columnHeaderHeight + Epsilon;
        if (forced || (fitsOnEmptyPage && _cursor + needed > _bodyBottom + Epsilon))
            NewPage();
    }

    private void OpenGroups(int from, IDataContext ctx)
    {
        for (var i = from; i < _def.Groups.Count; i++)
        {
            var name = _def.Groups[i].Name;
            foreach (var a in _aggregates)
            {
                if (a.Definition.Group == name) a.Reset();
            }
            PublishAggregates();
            EmitBands(Bands(BandType.GroupHeader, name), ctx);
        }
    }

    private void PublishAggregates()
    {
        foreach (var a in _aggregates)
            _vars[a.Definition.Name] = a.Value;
    }

    // ---- layout ----

    private void NewPage()
    {
        _pages.Add(new RenderedPage(_pages.Count + 1));
        _colIndex = 0;
        _rowHeight = 0;
        _cursor = _bodyTop;
        _contentTop = _cursor;
        if (_columnHeadersOn) EmitColumnHeaders();
    }

    /// <summary>Column headers use root fields only; they grow like other bands but are not split.</summary>
    private void EmitColumnHeaders()
    {
        foreach (var band in _columnHeaders)
        {
            if (!IsVisible(band, _rootCtx)) continue;

            var items = Prepare(band, _rootCtx, out var height);
            AddPrepared(_pages[^1], items, _cursor);
            _cursor += height;
        }
        _contentTop = _cursor;
    }

    private double CellWidth =>
        (_def.Page.Width - 2 * _def.Page.Margin - _def.Page.ColumnGap * (_def.Page.Columns - 1)) / _def.Page.Columns;

    /// <summary>Ends the current row of detail cells: the next band starts below its tallest cell.</summary>
    private void FlushRow()
    {
        if (_colIndex == 0) return;
        _cursor += _rowHeight;
        _colIndex = 0;
        _rowHeight = 0;
    }

    private void EmitCell(BandDefinition band, IDataContext ctx)
    {
        var items = Prepare(band, ctx, out var height);
        var bodyHeight = _bodyBottom - _bodyTop - _columnHeaderHeight;
        if (height > bodyHeight + Epsilon)
            throw new ReportException(
                $"{band.Type} band grew to {height:0.##} mm and does not fit in the page body ({bodyHeight:0.##} mm).");

        if (_colIndex == 0)
        {
            if (_cursor + height > _bodyBottom + Epsilon)
                NewPage();
            _rowTop = _cursor;
        }
        else if (_rowTop + Math.Max(_rowHeight, height) > _bodyBottom + Epsilon)
        {
            throw new ReportException("A detail cell grew taller than the space left on the page; give the band a larger height.");
        }

        AddPrepared(_pages[^1], items, _rowTop, _colIndex * (CellWidth + _def.Page.ColumnGap));
        _rowHeight = Math.Max(_rowHeight, height);
        if (++_colIndex == _def.Page.Columns) FlushRow();
    }

    private void EmitBands(IEnumerable<BandDefinition> bands, IDataContext ctx)
    {
        foreach (var band in bands)
        {
            if (!IsVisible(band, ctx)) continue;

            if (band.Type == BandType.Detail && _def.Page.Columns > 1)
            {
                EmitCell(band, ctx);
                continue;
            }

            FlushRow();

            var items = Prepare(band, ctx, out var height);

            var bodyHeight = _bodyBottom - _bodyTop - _columnHeaderHeight;
            if (height > bodyHeight + Epsilon)
                throw new ReportException(
                    $"{band.Type} band grew to {height:0.##} mm and does not fit in the page body ({bodyHeight:0.##} mm).");

            if (_cursor + height > _bodyBottom + Epsilon)
                NewPage();

            AddPrepared(_pages[^1], items, _cursor);
            _cursor += height;
        }
    }

    private static void CheckColor(BandDefinition band, string? color)
    {
        if (color is null) return;
        var digits = color.StartsWith('#') ? color[1..] : "";
        if ((digits.Length != 3 && digits.Length != 6) || !digits.All(Uri.IsHexDigit))
            throw new ReportException($"{band.Type} band: '{color}' is not a colour. Use #RGB or #RRGGBB.");
    }

    private bool IsVisible(BandDefinition band, IDataContext ctx) =>
        string.IsNullOrWhiteSpace(band.Visible) || Values.Truthy(Eval(band.Visible, ctx));

    private sealed record PreparedElement(
        ElementDefinition Def, ElementKind Kind, string Text, double Height, double LineHeight, byte[]? Image);

    /// <summary>Evaluates a band's elements. Wrapped text can make the band taller than its defined height.</summary>
    private List<PreparedElement> Prepare(BandDefinition band, IDataContext ctx, out double height)
    {
        height = band.Height;
        var items = new List<PreparedElement>(band.Elements.Count);
        PrepareElements(band, band.Elements, (EngineContext)ctx, 0, 0, items, ref height);

        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (item.Def.FitBand && item.Kind != ElementKind.Line)
                items[i] = item with { Height = Math.Max(item.Height, height - item.Def.Y) };
        }
        return items;
    }

    /// <summary>Evaluates elements into prepared ones, shifted by (dx, dy). Tables and lists expand into plain text elements.</summary>
    private void PrepareElements(BandDefinition band, IEnumerable<ElementDefinition> elements, EngineContext ctx,
        double dx, double dy, List<PreparedElement> items, ref double height)
    {
        foreach (var source in elements)
        {
            var type = source.Type.ToLowerInvariant();
            if (type == "table")
            {
                PrepareTable(source, ctx, dx, dy, items, ref height);
                continue;
            }
            if (type == "list")
            {
                PrepareList(band, source, ctx, dx, dy, items, ref height);
                continue;
            }

            var e = dx == 0 && dy == 0 ? source : source.WithOffset(dx, dy);
            var kind = KindOf(e.Type)!.Value;
            byte[]? image = null;
            string text;
            switch (kind)
            {
                case ElementKind.Line:
                case ElementKind.Rect:
                    text = "";
                    break;
                case ElementKind.Image when !string.IsNullOrWhiteSpace(e.Expression):
                {
                    // byte[] is the image itself; anything else is taken as a file path.
                    var value = Eval(e.Expression!, ctx);
                    image = value as byte[];
                    text = image is null ? Values.Str(value) : "";
                    break;
                }
                default:
                    text = !string.IsNullOrWhiteSpace(e.Expression) ? Values.Str(Eval(e.Expression, ctx)) : e.Text ?? "";
                    break;
            }
            if (kind == ElementKind.Barcode)
                CheckBarcode(band, text);
            else if (kind == ElementKind.QrCode)
                CheckQrCode(band, text);

            var elementHeight = e.Height;
            var lineHeight = 0.0;
            if (kind == ElementKind.Text && e.Wrap && e.Rotate == 0 && _measurer is not null)
            {
                var style = new TextStyle(e.Bold, e.Italic, e.FontSize, e.FontFamily);
                var lines = TextWrapper.Wrap(text, e.Width - 2 * e.Padding, _measurer, style);
                text = string.Join("\n", lines);
                lineHeight = _measurer.GetLineHeight(style);
                elementHeight = Math.Max(e.Height, lines.Count * lineHeight);
                height = Math.Max(height, e.Y + elementHeight);
            }
            items.Add(new PreparedElement(e, kind, text, elementHeight, lineHeight, image));
        }
    }

    private void PrepareTable(ElementDefinition e, EngineContext ctx, double dx, double dy,
        List<PreparedElement> items, ref double height)
    {
        var columns = e.Columns!;
        var widths = ColumnWidths(e);
        var rowHeight = e.RowHeight > 0 ? e.RowHeight : 5;
        var wrapOn = e.Wrap && _measurer is not null;
        var left = e.X + dx;
        var y = e.Y + dy;

        void AddRow(string[] texts, double minHeight, bool bold, string? fill)
        {
            var style = new TextStyle(bold, e.Italic, e.FontSize, e.FontFamily);
            var lineHeight = wrapOn ? _measurer!.GetLineHeight(style) : 0;
            var cells = new string[columns.Count];
            var rowH = minHeight;
            for (var i = 0; i < cells.Length; i++)
            {
                if (wrapOn)
                {
                    var lines = TextWrapper.Wrap(texts[i], widths[i] - 2 * e.Padding, _measurer!, style);
                    cells[i] = string.Join("\n", lines);
                    rowH = Math.Max(rowH, lines.Count * lineHeight);
                }
                else
                    cells[i] = texts[i];
            }

            var x = left;
            for (var i = 0; i < cells.Length; i++)
            {
                var cell = new ElementDefinition
                {
                    Type = "text", X = x, Y = y, Width = widths[i], Height = rowH, Align = columns[i].Align, VAlign = e.VAlign,
                    Bold = bold, Italic = e.Italic, FontSize = e.FontSize, FontFamily = e.FontFamily, Color = e.Color, Fill = fill, Border = e.Border,
                    Thickness = e.Thickness, Padding = e.Padding
                };
                items.Add(new PreparedElement(cell, ElementKind.Text, cells[i], rowH, lineHeight, null));
                x += widths[i];
            }
            y += rowH;
        }

        if (e.ShowHeader)
            AddRow(columns.Select(c => c.Header ?? "").ToArray(), e.HeaderHeight > 0 ? e.HeaderHeight : rowHeight, true, e.Fill);

        var index = 0;
        if (e.Rows is not null)
        {
            foreach (var row in e.Rows)
            {
                var texts = columns.Select((_, i) => i < row.Count ? StaticCell(row[i], ctx) : "").ToArray();
                AddRow(texts, rowHeight, e.Bold, index++ % 2 == 1 ? e.AltFill : null);
            }
        }
        else
        {
            foreach (var item in ctx.GetRows(e.Source!))
            {
                var rowCtx = ctx.WithRow(item);
                var texts = columns.Select(c => !string.IsNullOrWhiteSpace(c.Expression)
                    ? Values.Str(Eval(c.Expression!, rowCtx)) : c.Text ?? "").ToArray();
                AddRow(texts, rowHeight, e.Bold, index++ % 2 == 1 ? e.AltFill : null);
            }
        }

        height = Math.Max(height, y);
    }

    private string StaticCell(string cell, EngineContext ctx) =>
        cell.StartsWith('=') ? Values.Str(Eval(cell[1..], ctx)) : cell;

    private void PrepareList(BandDefinition band, ElementDefinition e, EngineContext ctx, double dx, double dy,
        List<PreparedElement> items, ref double height)
    {
        var rows = ctx.GetRows(e.Source!).ToList();
        var across = Math.Max(1, e.Across);
        var itemWidth = (e.Width - e.Gap * (across - 1)) / across;
        var itemHeight = e.ItemHeight > 0 ? e.ItemHeight : e.Height > 0 ? e.Height : 5;
        var left = e.X + dx;
        var top = e.Y + dy;

        for (var i = 0; i < rows.Count; i++)
        {
            var local = 0.0; // items have a fixed height: growth inside an item is ignored
            PrepareElements(band, e.Items!, ctx.WithRow(rows[i]),
                left + i % across * (itemWidth + e.Gap), top + i / across * (itemHeight + e.Gap), items, ref local);
        }

        var lines = (rows.Count + across - 1) / across;
        if (lines > 0) height = Math.Max(height, top + lines * itemHeight + (lines - 1) * e.Gap);
    }

    private static void CheckQrCode(BandDefinition band, string text)
    {
        if (text.Length == 0) return;
        try
        {
            QrCode.Encode(text);
        }
        catch (ArgumentException ex)
        {
            throw new ReportException($"{band.Type} band: cannot make a QR code from the text. {ex.Message}", ex);
        }
    }

    private static void CheckBarcode(BandDefinition band, string text)
    {
        if (text.Length == 0) return;
        try
        {
            Code128.Encode(text);
        }
        catch (ArgumentException ex)
        {
            throw new ReportException($"{band.Type} band: cannot make a barcode from '{text}'. {ex.Message}", ex);
        }
    }

    private void AddPrepared(RenderedPage page, List<PreparedElement> items, double top, double left = 0)
    {
        foreach (var item in items)
        {
            var e = item.Def;
            page.Add(new RenderedElement(
                _def.Page.Margin + left + e.X, top + e.Y, e.Width, item.Height, item.Text, e.Align, item.Kind,
                e.Thickness, item.LineHeight, item.Image, new TextStyle(e.Bold, e.Italic, e.FontSize, e.FontFamily),
                e.Color, e.Fill, e.Border, e.Padding, e.VAlign, e.Underline, e.Rotate));
        }
    }

    /// <summary>Used for page header/footer bands, whose height is fixed (wrapped text does not grow them).</summary>
    private void AddElements(RenderedPage page, BandDefinition band, IDataContext ctx, double top) =>
        AddPrepared(page, Prepare(band, ctx, out _), top);

    /// <summary>
    /// Page header/footer bands are evaluated last, once PageCount is known. They can use PageNumber, PageCount,
    /// root fields and report-wide aggregates (final values), but not row fields.
    /// </summary>
    private void FinishPages()
    {
        var vars = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var a in _aggregates)
        {
            if (a.Definition.Group is null) vars[a.Definition.Name] = a.Value;
        }
        vars[PageCountName] = (decimal)_pages.Count;

        var ctx = new EngineContext(null, _root, vars);
        var p = _def.Page;
        foreach (var page in _pages)
        {
            vars[PageNumberName] = (decimal)page.Number;

            var y = p.Margin;
            foreach (var band in _pageHeaders)
            {
                if (IsVisible(band, ctx)) AddElements(page, band, ctx, y);
                y += band.Height;
            }

            y = p.Height - p.Margin - _footerHeight;
            foreach (var band in _pageFooters)
            {
                if (IsVisible(band, ctx)) AddElements(page, band, ctx, y);
                y += band.Height;
            }
        }
    }
}
