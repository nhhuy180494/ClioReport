using System.Globalization;
using System.Xml.Linq;
using ClioReport.Model;

namespace ClioReport.Rdlc;

/// <summary>The imported definition and everything the importer had to skip or simplify.</summary>
public sealed record RdlcImportResult(ReportDefinition Definition, IReadOnlyList<string> Warnings);

/// <summary>
/// Imports an RDL/RDLC report (2008 or later, Tablix) into a <see cref="ReportDefinition"/>. It covers the common
/// subset: page size and margins, page header/footer, free text boxes and lines, one Tablix with header rows, row
/// groups, a details row and footer rows. Everything else is reported in <see cref="RdlcImportResult.Warnings"/>.
/// </summary>
public static class RdlcImporter
{
    public static RdlcImportResult ImportFile(string path)
    {
        using var stream = File.OpenRead(path);
        var result = Import(stream);
        result.Definition.Name = Path.GetFileNameWithoutExtension(path);
        return result;
    }

    public static RdlcImportResult Import(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return new Importer(XDocument.Load(stream)).Run();
    }

    public static RdlcImportResult ImportXml(string xml)
    {
        ArgumentNullException.ThrowIfNull(xml);
        return new Importer(XDocument.Parse(xml)).Run();
    }

    private sealed class Scope(string? name)
    {
        public string? Name { get; } = name;
        public List<int> Headers { get; } = new();
        public List<int> Footers { get; } = new();
        public bool SeenInner { get; set; }
    }

    private sealed record Row(double Height, List<XElement?> Cells, List<int> Spans);

    private sealed class Importer
    {
        private readonly XDocument _doc;
        private readonly List<string> _warnings = new();
        private readonly List<AggregateDefinition> _aggregates = new();
        private readonly HashSet<string> _groupNames = new(StringComparer.Ordinal);
        private readonly RdlcExpression _expr;
        private bool _partialBorders;

        public Importer(XDocument doc)
        {
            _doc = doc;
            _expr = new RdlcExpression(_aggregates, _groupNames);
        }

        public RdlcImportResult Run()
        {
            var root = _doc.Root ?? throw new ReportException("The file is empty.");
            if (root.Name.LocalName != "Report")
                throw new ReportException("Not an RDL report: the root element is not 'Report'.");
            if (root.Name.NamespaceName.Contains("2005", StringComparison.Ordinal))
                Warn("RDL 2005 file: tables are not imported. Open and save it in Visual Studio to upgrade it to Tablix.");

            var def = new ReportDefinition { Version = 1 };
            ReadPage(root, def);
            var pageNode = Child(root, "Page") ?? root;

            var header = Child(pageNode, "PageHeader") ?? Child(root, "PageHeader");
            var footer = Child(pageNode, "PageFooter") ?? Child(root, "PageFooter");
            if (header is not null) def.Bands.Add(PageBand(BandType.PageHeader, header));

            ReadBody(root, def, out var footerBand, out var detailBands);
            def.Bands.AddRange(detailBands);
            if (footerBand is not null) def.Bands.Add(footerBand);
            if (footer is not null) def.Bands.Add(PageBand(BandType.PageFooter, footer));

            def.Aggregates.AddRange(_aggregates);
            return new RdlcImportResult(def, _warnings);
        }

        private void Warn(string message) => _warnings.Add(message);

        // ---- page -------------------------------------------------------------------------------------------

        private void ReadPage(XElement root, ReportDefinition def)
        {
            var page = Child(root, "Page") ?? root;
            def.Page.Width = Length(page, "PageWidth", 8.5 * 25.4);
            def.Page.Height = Length(page, "PageHeight", 11 * 25.4);
            def.Page.Margin = Length(page, "LeftMargin", 0);
            var other = new[] { "RightMargin", "TopMargin", "BottomMargin" }.Select(n => Length(page, n, 0)).ToArray();
            if (other.Any(m => Math.Abs(m - def.Page.Margin) > 0.01))
                Warn($"Margins differ per side; ClioReport has one margin, so {def.Page.Margin:0.##} mm (left) is used.");
            var columns = Val(page, "Columns");
            if (columns is not null && columns != "1") Warn("Report columns are not imported (use page.columns for labels).");
        }

        private static double Length(XElement? parent, string name, double fallback) =>
            RdlcUnits.TryMillimetres(Val(parent, name), out var mm) ? mm : fallback;

        private BandDefinition PageBand(BandType type, XElement node)
        {
            var band = new BandDefinition { Type = type, Height = Length(node, "Height", 10) };
            foreach (var item in Children(Child(node, "ReportItems"), null))
                band.Elements.AddRange(ConvertItem(item, 0, 0, null));
            return band;
        }

        // ---- body -------------------------------------------------------------------------------------------

        private void ReadBody(XElement root, ReportDefinition def, out BandDefinition? footerBand, out List<BandDefinition> bands)
        {
            footerBand = null;
            bands = new List<BandDefinition>();

            var sections = Children(Child(root, "ReportSections"), "ReportSection").ToList();
            var body = sections.Count > 0 ? Child(sections[0], "Body") : Child(root, "Body");
            if (sections.Count > 1) Warn("Only the first report section is imported.");
            if (body is null) return;

            var items = Children(Child(body, "ReportItems"), null).ToList();
            var tablices = items.Where(i => i.Name.LocalName is "Tablix" or "Table").ToList();
            var tablix = tablices.FirstOrDefault(i => i.Name.LocalName == "Tablix");
            foreach (var other in tablices.Where(t => t != tablix))
                Warn($"{other.Name.LocalName} '{Attr(other, "Name")}' is not imported.");
            if (tablices.Count(t => t.Name.LocalName == "Tablix") > 1)
                Warn("Only the first Tablix is imported.");

            var tablixTop = tablix is null ? double.MaxValue : Length(tablix, "Top", 0);
            var tablixBottom = tablix is null ? 0 : tablixTop + Length(tablix, "Height", 0);

            var headerElements = new List<ElementDefinition>();
            var footerElements = new List<ElementDefinition>();
            foreach (var item in items.Where(i => !tablices.Contains(i)))
            {
                var top = Length(item, "Top", 0);
                var converted = ConvertItem(item, 0, 0, null);
                if (top < tablixTop) headerElements.AddRange(converted);
                else
                {
                    foreach (var e in converted) e.Y -= tablixBottom;
                    footerElements.AddRange(converted);
                }
            }

            if (headerElements.Count > 0)
            {
                var height = tablix is null ? headerElements.Max(e => e.Y + e.Height) : tablixTop;
                bands.Add(new BandDefinition { Type = BandType.ReportHeader, Height = Math.Max(height, 0.1), Elements = headerElements });
            }

            List<ElementDefinition>? tablixFooter = null;
            double tablixFooterHeight = 0;
            if (tablix is not null)
                ReadTablix(tablix, def, bands, out tablixFooter, out tablixFooterHeight);

            var footerItems = new List<ElementDefinition>();
            if (tablixFooter is not null) footerItems.AddRange(tablixFooter);
            foreach (var e in footerElements)
            {
                e.Y += tablixFooterHeight;
                footerItems.Add(e);
            }
            if (footerItems.Count > 0)
                footerBand = new BandDefinition
                {
                    Type = BandType.ReportFooter, Height = Math.Max(footerItems.Max(e => e.Y + e.Height), 0.1), Elements = footerItems
                };
        }

        // ---- tablix -----------------------------------------------------------------------------------------

        private void ReadTablix(XElement tablix, ReportDefinition def, List<BandDefinition> bands,
            out List<ElementDefinition>? reportFooter, out double reportFooterHeight)
        {
            reportFooter = null;
            reportFooterHeight = 0;

            def.DataSource = Val(tablix, "DataSetName") ?? "";
            if (def.DataSource.Length == 0) Warn("The Tablix has no DataSetName; set ReportDefinition.DataSource yourself.");

            var tablixBody = Child(tablix, "TablixBody");
            var columns = Children(Child(tablixBody, "TablixColumns"), "TablixColumn")
                .Select(c => Length(c, "Width", 0)).ToList();
            var rows = new List<Row>();
            foreach (var row in Children(Child(tablixBody, "TablixRows"), "TablixRow"))
            {
                var cells = new List<XElement?>();
                var spans = new List<int>();
                foreach (var cell in Children(Child(row, "TablixCells"), "TablixCell"))
                {
                    var contents = Child(cell, "CellContents");
                    cells.Add(contents is null ? null : Children(contents, null).FirstOrDefault(c => c.Name.LocalName != "ColSpan" && c.Name.LocalName != "RowSpan"));
                    spans.Add(int.TryParse(Val(contents, "ColSpan"), out var span) && span > 0 ? span : 1);
                }
                rows.Add(new Row(Length(row, "Height", 5), cells, spans));
            }

            if (Child(Child(tablix, "TablixColumnHierarchy"), "TablixMembers")?.Descendants().Any(d => d.Name.LocalName == "Group") == true)
                Warn("Column groups are not imported.");

            // Row hierarchy: header rows, row groups, the details row and footer rows, in row order.
            var report = new Scope(null);
            var scopes = new List<Scope>();
            var rowIndex = 0;
            var detailRow = -1;
            var headerWidthByDepth = new Dictionary<int, double>();

            void Walk(IEnumerable<XElement> members, Scope scope, int depth)
            {
                foreach (var member in members)
                {
                    var group = Child(member, "Group");
                    var kids = Children(Child(member, "TablixMembers"), "TablixMember").ToList();
                    var tablixHeader = Child(member, "TablixHeader");
                    if (tablixHeader is not null)
                        headerWidthByDepth[depth] = Math.Max(headerWidthByDepth.GetValueOrDefault(depth), Length(tablixHeader, "Size", 0));

                    var expressions = group is null ? new List<XElement>() : group.Descendants().Where(d => d.Name.LocalName == "GroupExpression").ToList();
                    if (group is not null && expressions.Count > 0)
                    {
                        var name = Attr(group, "Name") ?? "group" + (scopes.Count + 1);
                        var expression = _expr.Translate(expressions[0].Value, name, out var problem);
                        if (expression is null)
                        {
                            Warn($"Group '{name}': {problem}; the group is ignored.");
                            expression = "''";
                        }
                        if (expressions.Count > 1) Warn($"Group '{name}': only the first group expression is used.");
                        _groupNames.Add(name);
                        var inner = new Scope(name);
                        scopes.Add(inner);
                        def.Groups.Add(new GroupDefinition
                        {
                            Name = name, Expression = expression,
                            PageBreakBefore = group.Descendants().Any(d => d.Name.LocalName == "BreakLocation" && d.Value.Trim() is "Start" or "StartAndEnd")
                        });
                        if (kids.Count == 0) { detailRow = rowIndex++; inner.SeenInner = true; }
                        else Walk(kids, inner, depth + 1);
                        scope.SeenInner = true;
                    }
                    else if (group is not null)
                    {
                        if (kids.Count == 0) { detailRow = rowIndex++; scope.SeenInner = true; }
                        else Walk(kids, scope, depth + 1);
                        scope.SeenInner = true;
                    }
                    else if (kids.Count > 0)
                        Walk(kids, scope, depth + 1);
                    else
                    {
                        (scope.SeenInner ? scope.Footers : scope.Headers).Add(rowIndex++);
                    }
                }
            }

            Walk(Children(Child(Child(tablix, "TablixRowHierarchy"), "TablixMembers"), "TablixMember"), report, 0);

            var left = Length(tablix, "Left", 0) + headerWidthByDepth.Values.Sum();
            if (headerWidthByDepth.Values.Any(w => w > 0))
                Warn("Row group header cells are not imported; the group columns are left blank.");

            BandDefinition? Stack(BandType type, string? group, IEnumerable<int> indices, string? scopeGroup)
            {
                var elements = new List<ElementDefinition>();
                double y = 0;
                foreach (var i in indices)
                {
                    if (i >= rows.Count) continue;
                    var row = rows[i];
                    var x = left;
                    var column = 0;
                    for (var c = 0; c < row.Cells.Count && column < columns.Count; c++)
                    {
                        var span = Math.Min(row.Spans[c], columns.Count - column);
                        var width = columns.Skip(column).Take(span).Sum();
                        var cell = row.Cells[c];
                        if (cell is not null)
                        {
                            if (cell.Name.LocalName == "Textbox")
                                elements.Add(ConvertTextbox(cell, x, y, width, row.Height, scopeGroup));
                            else
                                elements.AddRange(ConvertItem(cell, x, y, scopeGroup));
                        }
                        x += width;
                        column += span;
                    }
                    y += row.Height;
                }
                return elements.Count == 0 && y == 0 ? null
                    : new BandDefinition { Type = type, Group = group, Height = y, Elements = elements };
            }

            var columnHeader = Stack(BandType.ColumnHeader, null, report.Headers, null);
            if (columnHeader is not null) bands.Add(columnHeader);
            foreach (var scope in scopes)
            {
                var band = Stack(BandType.GroupHeader, scope.Name, scope.Headers, scope.Name);
                if (band is not null) bands.Add(band);
            }
            var innermost = scopes.Count > 0 ? scopes[^1].Name : null;
            if (detailRow >= 0)
            {
                var detail = Stack(BandType.Detail, null, new[] { detailRow }, innermost);
                if (detail is not null) bands.Add(detail);
            }
            else
                Warn("The Tablix has no details row.");
            for (var i = scopes.Count - 1; i >= 0; i--)
            {
                var band = Stack(BandType.GroupFooter, scopes[i].Name, scopes[i].Footers, scopes[i].Name);
                if (band is not null) bands.Add(band);
            }

            var footerBand = Stack(BandType.ReportFooter, null, report.Footers, null);
            if (footerBand is not null)
            {
                reportFooter = footerBand.Elements;
                reportFooterHeight = footerBand.Height;
            }
        }

        // ---- report items -----------------------------------------------------------------------------------

        private List<ElementDefinition> ConvertItem(XElement item, double dx, double dy, string? scope)
        {
            var result = new List<ElementDefinition>();
            var name = Attr(item, "Name") ?? item.Name.LocalName;
            var x = dx + Length(item, "Left", 0);
            var y = dy + Length(item, "Top", 0);
            var w = Length(item, "Width", 0);
            var h = Length(item, "Height", 0);

            switch (item.Name.LocalName)
            {
                case "Textbox":
                    result.Add(ConvertTextbox(item, x, y, w, h, scope));
                    break;
                case "Line":
                {
                    var line = new ElementDefinition { Type = "line", X = x, Y = y, Width = w, Height = h };
                    var style = Child(item, "Style");
                    var thickness = RdlcUnits.TryMillimetres(Val(Child(style, "Border"), "Width"), out var t) ? t : 0.26;
                    line.Thickness = thickness;
                    line.Color = RdlcUnits.Color(Val(Child(style, "Border"), "Color"));
                    result.Add(line);
                    break;
                }
                case "Rectangle":
                {
                    var border = Child(Child(item, "Style"), "Border");
                    if (border is not null && Val(border, "Style") is not (null or "None"))
                        result.Add(new ElementDefinition { Type = "rect", X = x, Y = y, Width = w, Height = h, Color = RdlcUnits.Color(Val(border, "Color")) });
                    foreach (var child in Children(Child(item, "ReportItems"), null))
                        result.AddRange(ConvertItem(child, x, y, scope));
                    break;
                }
                case "Image":
                    Warn($"Image '{name}' is not imported (use an image element bound to a field).");
                    break;
                default:
                    Warn($"{item.Name.LocalName} '{name}' is not supported and was skipped.");
                    break;
            }
            return result;
        }

        private ElementDefinition ConvertTextbox(XElement tb, double x, double y, double w, double h, string? scope)
        {
            var name = Attr(tb, "Name") ?? "Textbox";
            var e = new ElementDefinition { X = x, Y = y, Width = w, Height = h };

            var paragraphs = Children(Child(tb, "Paragraphs"), "Paragraph").ToList();
            var runs = paragraphs.SelectMany(p => Children(Child(p, "TextRuns"), "TextRun")).ToList();
            var tbStyle = Child(tb, "Style");
            var runStyle = runs.Count > 0 ? Child(runs[0], "Style") : null;
            var paraStyle = paragraphs.Count > 0 ? Child(paragraphs[0], "Style") : null;
            string? Prop(string n) => Val(runStyle, n) ?? Val(paraStyle, n) ?? Val(tbStyle, n);

            var values = runs.Count > 0
                ? runs.Select(r => Val(r, "Value") ?? "").ToList()
                : new List<string> { Val(tb, "Value") ?? "" };
            if (paragraphs.Count > 1) Warn($"Textbox '{name}': several paragraphs are joined with a space.");

            var format = Prop("Format");
            var pieces = new List<string>();
            var anyExpression = false;
            foreach (var value in values)
            {
                if (value.StartsWith('='))
                {
                    var translated = _expr.Translate(value, scope, out var problem);
                    if (translated is null)
                    {
                        Warn($"Textbox '{name}': {problem}; expression `{value}` was skipped.");
                        continue;
                    }
                    if (!string.IsNullOrEmpty(format) && values.Count == 1)
                        translated = $"Format({translated}, '{format.Replace("'", "\\'")}')";
                    pieces.Add("(" + translated + ")");
                    anyExpression = true;
                }
                else
                    pieces.Add("'" + value.Replace("\\", "\\\\").Replace("'", "\\'") + "'");
            }

            if (!anyExpression)
                e.Text = string.Join("", values.Where(v => !v.StartsWith('=')));
            else
                e.Expression = pieces.Count == 1 ? pieces[0][1..^1] : "'' + " + string.Join(" + ", pieces);

            e.Align = Prop("TextAlign") switch { "Center" => TextAlign.Center, "Right" => TextAlign.Right, _ => TextAlign.Left };
            e.VAlign = Prop("VerticalAlign") switch { "Middle" => VerticalAlign.Middle, "Bottom" => VerticalAlign.Bottom, _ => VerticalAlign.Top };

            var weight = Prop("FontWeight");
            e.Bold = weight is "Bold" or "Bolder" or "600" or "700" or "800" or "900";
            e.Italic = Prop("FontStyle") == "Italic";
            if (RdlcUnits.TryPoints(Prop("FontSize"), out var size)) e.FontSize = Math.Round(size, 1);
            e.Underline = Prop("TextDecoration") == "Underline";
            e.Rotate = Prop("WritingMode") switch { "Rotate270" => 270, "Vertical" => 90, _ => 0 };

            var color = Prop("Color");
            e.Color = RdlcUnits.Color(color);
            if (color is not null && e.Color is null && !color.StartsWith('='))
                Warn($"Textbox '{name}': colour '{color}' is not supported.");
            e.Fill = RdlcUnits.Color(Val(tbStyle, "BackgroundColor"));

            var border = Child(tbStyle, "Border");
            var sides = new[] { "TopBorder", "BottomBorder", "LeftBorder", "RightBorder" }
                .Select(n => Child(tbStyle, n)).Where(b => b is not null && Val(b, "Style") is not (null or "None")).ToList();
            if (border is not null && Val(border, "Style") is not (null or "None"))
                e.Border = true;
            else if (sides.Count > 0)
            {
                e.Border = true;
                if (sides.Count < 4 && !_partialBorders)
                {
                    _partialBorders = true;
                    Warn("Cells with borders on only some sides get a border on all sides.");
                }
            }
            if (e.Border && RdlcUnits.TryMillimetres(Val(border ?? sides[0], "Width"), out var thickness) && thickness > 0)
                e.Thickness = thickness;

            if (RdlcUnits.TryMillimetres(Val(tbStyle, "PaddingLeft"), out var padding) && padding > 0)
                e.Padding = Math.Min(padding, Math.Max(0, w / 2 - 0.01));

            var hidden = Val(Child(tb, "Visibility"), "Hidden");
            if (hidden is not null && hidden.Equals("true", StringComparison.OrdinalIgnoreCase))
            {
                e.Text = "";
                e.Expression = null;
            }
            else if (hidden is not null && hidden.StartsWith('='))
                Warn($"Textbox '{name}': conditional visibility is not imported.");

            return e;
        }

        // ---- xml helpers ------------------------------------------------------------------------------------

        private static XElement? Child(XElement? parent, string name) =>
            parent?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

        private static IEnumerable<XElement> Children(XElement? parent, string? name) =>
            parent is null ? Enumerable.Empty<XElement>() : parent.Elements().Where(e => name is null || e.Name.LocalName == name);

        private static string? Val(XElement? parent, string name) => Child(parent, name)?.Value.Trim();

        private static string? Attr(XElement el, string name) => el.Attribute(name)?.Value;
    }
}
