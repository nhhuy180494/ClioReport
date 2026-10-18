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
    private readonly Dictionary<string, Expression> _expressions = new();
    private readonly Dictionary<string, object?> _vars = new(StringComparer.Ordinal);
    private readonly List<AggregateState> _aggregates = new();
    private readonly List<RenderedPage> _pages = new();

    private ILookup<(BandType, string), BandDefinition> _bands = null!;
    private List<BandDefinition> _pageHeaders = new();
    private List<BandDefinition> _pageFooters = new();
    private double _footerHeight;
    private double _bodyTop;
    private double _bodyBottom;
    private double _cursor;

    private object?[]? _lastKeys;
    private IDataContext? _lastCtx;

    public ReportRunner(ReportDefinition definition, IDataContext root)
    {
        _def = definition;
        _root = root;
    }

    public RenderedReport Execute()
    {
        var p = _def.Page;
        _pageHeaders = _def.Bands.Where(b => b.Type == BandType.PageHeader).ToList();
        _pageFooters = _def.Bands.Where(b => b.Type == BandType.PageFooter).ToList();
        _footerHeight = _pageFooters.Sum(b => b.Height);
        _bodyTop = p.Margin + _pageHeaders.Sum(b => b.Height);
        _bodyBottom = p.Height - p.Margin - _footerHeight;
        _bands = _def.Bands.ToLookup(Key);

        Validate();
        Prepare();

        NewPage();
        var rootCtx = new EngineContext(null, _root, _vars);
        EmitBands(Bands(BandType.ReportHeader), rootCtx);

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
        _ => null
    };

    private IEnumerable<BandDefinition> Bands(BandType type, string group = "") => _bands[(type, group)];

    private void Validate()
    {
        var p = _def.Page;
        if (p.Width <= 0 || p.Height <= 0 || p.Margin < 0 || p.Width - 2 * p.Margin <= 0)
            throw new ReportException("Invalid page size or margin.");

        var bodyHeight = _bodyBottom - _bodyTop;
        if (bodyHeight <= 0)
            throw new ReportException("Page header/footer bands leave no room for the page body.");

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
            if (!isPageBand && b.Height > bodyHeight + Epsilon)
                throw new ReportException(
                    $"{b.Type} band (height {b.Height}) does not fit in the page body (height {bodyHeight}).");

            foreach (var e in b.Elements)
            {
                if (KindOf(e.Type) is null)
                    throw new ReportException($"Unsupported element type '{e.Type}'.");
            }
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
        if (from >= _def.Groups.Count || _cursor <= _bodyTop + Epsilon) return;

        var forced = _lastCtx is not null && _def.Groups.Skip(from).Any(g => g.PageBreakBefore);

        var needed = Bands(BandType.Detail).Sum(b => b.Height);
        for (var i = from; i < _def.Groups.Count; i++)
            needed += Bands(BandType.GroupHeader, _def.Groups[i].Name).Sum(b => b.Height);

        var fitsOnEmptyPage = needed <= _bodyBottom - _bodyTop + Epsilon;
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
        _cursor = _bodyTop;
    }

    private void EmitBands(IEnumerable<BandDefinition> bands, IDataContext ctx)
    {
        foreach (var band in bands)
        {
            if (_cursor + band.Height > _bodyBottom + Epsilon)
                NewPage();

            AddElements(_pages[^1], band, ctx, _cursor);
            _cursor += band.Height;
        }
    }

    private void AddElements(RenderedPage page, BandDefinition band, IDataContext ctx, double top)
    {
        foreach (var e in band.Elements)
        {
            var kind = KindOf(e.Type)!.Value;
            var text = kind != ElementKind.Text ? ""
                : !string.IsNullOrWhiteSpace(e.Expression) ? Values.Str(Eval(e.Expression, ctx))
                : e.Text ?? "";
            page.Add(new RenderedElement(
                _def.Page.Margin + e.X, top + e.Y, e.Width, e.Height, text, e.Align, kind, e.Thickness));
        }
    }

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
                AddElements(page, band, ctx, y);
                y += band.Height;
            }

            y = p.Height - p.Margin - _footerHeight;
            foreach (var band in _pageFooters)
            {
                AddElements(page, band, ctx, y);
                y += band.Height;
            }
        }
    }
}
