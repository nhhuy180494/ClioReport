using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ClioReport.Model;

namespace ClioReport.Xlsx;

/// <summary>
/// Writes the pages of a <see cref="RenderedReport"/> as one worksheet. Text elements become cells: elements that start at
/// (nearly) the same top edge form a row and those that start at (nearly) the same left edge form a column, so the cells of
/// a table line up as a grid. Pages follow each other with an empty row between them. Lines, rectangles, images, barcodes
/// and QR codes are not exported. Plain numbers (such as <c>1500.4</c>) become numeric cells; text that was formatted, for
/// instance with <c>Format(x, 'N0')</c>, stays text.
/// </summary>
public static class XlsxReportRenderer
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string PackageRel = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string ContentTypes = "http://schemas.openxmlformats.org/package/2006/content-types";

    private static readonly Regex PlainNumber = new(@"^-?(0|[1-9][0-9]{0,14})(\.[0-9]+)?$", RegexOptions.CultureInvariant);

    public static byte[] ToBytes(RenderedReport report, XlsxRenderOptions? options = null)
    {
        using var stream = new MemoryStream();
        Render(report, stream, options);
        return stream.ToArray();
    }

    public static void Render(RenderedReport report, string path, XlsxRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(path);
        using var stream = File.Create(path);
        Render(report, stream, options);
    }

    public static void Render(RenderedReport report, Stream stream, XlsxRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(stream);
        options ??= new XlsxRenderOptions();

        var sheet = BuildSheet(report, options);
        var styles = new StyleTable();
        foreach (var cell in sheet.Cells) cell.StyleIndex = styles.IndexOf(cell.Style);

        using var zip = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true);
        Part(zip, "[Content_Types].xml", w => WriteContentTypes(w));
        Part(zip, "_rels/.rels", w => WriteRootRels(w));
        Part(zip, "xl/workbook.xml", w => WriteWorkbook(w, SheetName(options.SheetName)));
        Part(zip, "xl/_rels/workbook.xml.rels", w => WriteWorkbookRels(w));
        Part(zip, "xl/styles.xml", w => styles.Write(w));
        Part(zip, "xl/worksheets/sheet1.xml", w => WriteSheet(w, sheet, report.PageWidth > report.PageHeight));
    }

    // ---------------------------------------------------------------- layout -> grid

    private sealed class Cell
    {
        public int Row, Col;
        public string Text = "";
        public CellStyle Style;
        public int StyleIndex;
    }

    private sealed class Sheet
    {
        public List<Cell> Cells = new();
        public Dictionary<int, double> RowHeightsPt = new();
        public double[] ColumnWidthsMm = Array.Empty<double>();
    }

    private static Sheet BuildSheet(RenderedReport report, XlsxRenderOptions options)
    {
        var items = new List<(int Page, RenderedElement E)>();
        foreach (var page in report.Pages)
            foreach (var e in page.Elements)
                if (e.Kind == ElementKind.Text && !string.IsNullOrWhiteSpace(e.Text))
                    items.Add((page.Number, e));

        var sheet = new Sheet();
        if (items.Count == 0) return sheet;

        // columns: cluster the left edges
        var xs = items.Select(i => i.E.X).OrderBy(x => x).ToList();
        var colStarts = new List<double>();
        foreach (var x in xs)
            if (colStarts.Count == 0 || x - colStarts[^1] > options.ColumnTolerance) colStarts.Add(x);
        int ColumnOf(double x)
        {
            var c = 0;
            for (var i = 0; i < colStarts.Count; i++)
                if (colStarts[i] <= x + 1e-9) c = i;
            return c;
        }

        // rows: cluster the top edges page by page, with a blank row between pages
        var ordered = items.OrderBy(i => i.Page).ThenBy(i => i.E.Y).ThenBy(i => i.E.X).ToList();
        var row = 0;
        var rowStart = double.NaN;
        var lastPage = -1;
        var cells = new Dictionary<(int Row, int Col), Cell>();
        var minWidth = new double[colStarts.Count];
        Array.Fill(minWidth, double.MaxValue);

        foreach (var (page, e) in ordered)
        {
            if (page != lastPage)
            {
                row += lastPage < 0 ? 1 : 2;
                lastPage = page;
                rowStart = e.Y;
            }
            else if (e.Y - rowStart > options.RowTolerance)
            {
                row++;
                rowStart = e.Y;
            }

            var col = ColumnOf(e.X);
            minWidth[col] = Math.Min(minWidth[col], Math.Max(e.Width, 1));
            var style = CellStyle.From(e);
            var key = (row, col);
            if (cells.TryGetValue(key, out var existing))
                existing.Text += " " + e.Text;
            else
                cells[key] = new Cell { Row = row, Col = col, Text = e.Text, Style = style };

            var heightPt = Math.Max(e.Height, 1) * 72 / 25.4;
            sheet.RowHeightsPt[row] = Math.Max(sheet.RowHeightsPt.GetValueOrDefault(row), heightPt);
        }

        sheet.Cells = cells.Values.OrderBy(c => c.Row).ThenBy(c => c.Col).ToList();
        sheet.ColumnWidthsMm = new double[colStarts.Count];
        for (var i = 0; i < colStarts.Count; i++)
        {
            var width = minWidth[i] == double.MaxValue ? 20 : minWidth[i];
            if (i + 1 < colStarts.Count) width = Math.Min(width, colStarts[i + 1] - colStarts[i]);
            sheet.ColumnWidthsMm[i] = Math.Max(width, 5);
        }
        return sheet;
    }

    // ---------------------------------------------------------------- xml parts

    private static void Part(ZipArchive zip, string name, Action<XmlWriter> write)
    {
        var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
        using var stream = entry.Open();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false, CloseOutput = false };
        using var writer = XmlWriter.Create(stream, settings);
        writer.WriteStartDocument(true);
        write(writer);
        writer.WriteEndDocument();
    }

    private static void WriteContentTypes(XmlWriter w)
    {
        w.WriteStartElement("Types", ContentTypes);
        Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        Default(w, "xml", "application/xml");
        Override(w, "/xl/workbook.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml");
        Override(w, "/xl/worksheets/sheet1.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml");
        Override(w, "/xl/styles.xml", "application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml");
        w.WriteEndElement();

        static void Default(XmlWriter w, string ext, string type)
        {
            w.WriteStartElement("Default", ContentTypes);
            w.WriteAttributeString("Extension", ext);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }

        static void Override(XmlWriter w, string part, string type)
        {
            w.WriteStartElement("Override", ContentTypes);
            w.WriteAttributeString("PartName", part);
            w.WriteAttributeString("ContentType", type);
            w.WriteEndElement();
        }
    }

    private static void WriteRelationship(XmlWriter w, string id, string type, string target)
    {
        w.WriteStartElement("Relationship", PackageRel);
        w.WriteAttributeString("Id", id);
        w.WriteAttributeString("Type", type);
        w.WriteAttributeString("Target", target);
        w.WriteEndElement();
    }

    private static void WriteRootRels(XmlWriter w)
    {
        w.WriteStartElement("Relationships", PackageRel);
        WriteRelationship(w, "rId1", RelNs + "/officeDocument", "xl/workbook.xml");
        w.WriteEndElement();
    }

    private static void WriteWorkbookRels(XmlWriter w)
    {
        w.WriteStartElement("Relationships", PackageRel);
        WriteRelationship(w, "rId1", RelNs + "/worksheet", "worksheets/sheet1.xml");
        WriteRelationship(w, "rId2", RelNs + "/styles", "styles.xml");
        w.WriteEndElement();
    }

    private static void WriteWorkbook(XmlWriter w, string sheetName)
    {
        w.WriteStartElement("workbook", Main);
        w.WriteStartElement("sheets", Main);
        w.WriteStartElement("sheet", Main);
        w.WriteAttributeString("name", sheetName);
        w.WriteAttributeString("sheetId", "1");
        w.WriteAttributeString("r", "id", RelNs, "rId1");
        w.WriteEndElement();
        w.WriteEndElement();
        w.WriteEndElement();
    }

    private static string SheetName(string? name)
    {
        var cleaned = new string((name ?? "").Where(c => !"[]:*?/\\".Contains(c) && !char.IsControl(c)).ToArray()).Trim();
        if (cleaned.Length > 31) cleaned = cleaned[..31];
        return cleaned.Length == 0 ? "Report" : cleaned;
    }

    private static void WriteSheet(XmlWriter w, Sheet sheet, bool landscape)
    {
        w.WriteStartElement("worksheet", Main);

        w.WriteStartElement("sheetViews", Main);
        w.WriteStartElement("sheetView", Main);
        w.WriteAttributeString("workbookViewId", "0");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("sheetFormatPr", Main);
        w.WriteAttributeString("defaultRowHeight", "15");
        w.WriteEndElement();

        if (sheet.ColumnWidthsMm.Length > 0)
        {
            w.WriteStartElement("cols", Main);
            for (var i = 0; i < sheet.ColumnWidthsMm.Length; i++)
            {
                w.WriteStartElement("col", Main);
                w.WriteAttributeString("min", (i + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("max", (i + 1).ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("width", Math.Clamp(sheet.ColumnWidthsMm[i] / 2.0, 3, 100).ToString("0.##", CultureInfo.InvariantCulture));
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        w.WriteStartElement("sheetData", Main);
        foreach (var rowGroup in sheet.Cells.GroupBy(c => c.Row))
        {
            w.WriteStartElement("row", Main);
            w.WriteAttributeString("r", rowGroup.Key.ToString(CultureInfo.InvariantCulture));
            var height = Math.Clamp(sheet.RowHeightsPt.GetValueOrDefault(rowGroup.Key, 15), 8, 409);
            w.WriteAttributeString("ht", height.ToString("0.##", CultureInfo.InvariantCulture));
            w.WriteAttributeString("customHeight", "1");
            foreach (var cell in rowGroup.OrderBy(c => c.Col))
                WriteCell(w, cell);
            w.WriteEndElement();
        }
        w.WriteEndElement();

        w.WriteStartElement("pageMargins", Main);
        foreach (var side in new[] { "left", "right", "top", "bottom" }) w.WriteAttributeString(side, "0.5");
        w.WriteAttributeString("header", "0.3");
        w.WriteAttributeString("footer", "0.3");
        w.WriteEndElement();

        w.WriteStartElement("pageSetup", Main);
        w.WriteAttributeString("paperSize", "9");
        w.WriteAttributeString("orientation", landscape ? "landscape" : "portrait");
        w.WriteEndElement();

        w.WriteEndElement();
    }

    private static void WriteCell(XmlWriter w, Cell cell)
    {
        var text = Clean(cell.Text);
        w.WriteStartElement("c", Main);
        w.WriteAttributeString("r", ColumnName(cell.Col) + (cell.Row).ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("s", cell.StyleIndex.ToString(CultureInfo.InvariantCulture));
        if (PlainNumber.IsMatch(text))
        {
            w.WriteElementString("v", Main, text);
        }
        else
        {
            w.WriteAttributeString("t", "inlineStr");
            w.WriteStartElement("is", Main);
            w.WriteStartElement("t", Main);
            w.WriteAttributeString("xml", "space", null, "preserve");
            w.WriteString(text);
            w.WriteEndElement();
            w.WriteEndElement();
        }
        w.WriteEndElement();
    }

    private static string ColumnName(int zeroBased)
    {
        var n = zeroBased + 1;
        var name = "";
        while (n > 0)
        {
            n--;
            name = (char)('A' + n % 26) + name;
            n /= 26;
        }
        return name;
    }

    private static string Clean(string text)
    {
        var sb = new StringBuilder(text.Length);
        foreach (var c in text)
            if (char.IsSurrogate(c) || XmlConvert.IsXmlChar(c)) sb.Append(c);
        return sb.ToString();
    }

    // ---------------------------------------------------------------- styles

    internal readonly record struct CellStyle(
        bool Bold, bool Italic, double FontSize, string? Color, string? Fill, bool Border,
        TextAlign Align, VerticalAlign VAlign, bool Wrap)
    {
        public static CellStyle From(RenderedElement e) => new(
            e.Style.Bold, e.Style.Italic, e.Style.FontSize, Rgb(e.Color), Rgb(e.Fill), e.Border,
            e.Align, e.VAlign, e.Text.Contains('\n'));

        private static string? Rgb(string? hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#') return null;
            var digits = hex[1..].ToUpperInvariant();
            if (digits.Length == 3) digits = string.Concat(digits.Select(d => new string(d, 2)));
            return digits.Length == 6 && digits.All(Uri.IsHexDigit) ? "FF" + digits : null;
        }
    }

    private sealed class StyleTable
    {
        private readonly List<CellStyle> _styles = new() { default };
        private readonly Dictionary<CellStyle, int> _index = new();

        public int IndexOf(CellStyle style)
        {
            if (_index.TryGetValue(style, out var i)) return i;
            _styles.Add(style);
            return _index[style] = _styles.Count - 1;
        }

        public void Write(XmlWriter w)
        {
            var fonts = new List<(bool B, bool I, double Size, string Name, string? Color)>();
            var fills = new List<string?> { null, null };           // 0 none, 1 gray125 (both required by Excel)
            int FontId(CellStyle s)
            {
                var f = (s.Bold, s.Italic, s.FontSize > 0 ? s.FontSize : 10, "Arial", s.Color);
                var i = fonts.IndexOf(f);
                if (i >= 0) return i;
                fonts.Add(f);
                return fonts.Count - 1;
            }
            int FillId(CellStyle s)
            {
                if (s.Fill is null) return 0;
                var i = fills.IndexOf(s.Fill, 2);
                if (i >= 0) return i;
                fills.Add(s.Fill);
                return fills.Count - 1;
            }

            var xfs = _styles.Select(s => (Font: FontId(s), Fill: FillId(s), Style: s)).ToList();

            w.WriteStartElement("styleSheet", Main);

            w.WriteStartElement("fonts", Main);
            w.WriteAttributeString("count", fonts.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var f in fonts)
            {
                w.WriteStartElement("font", Main);
                if (f.B) Empty(w, "b");
                if (f.I) Empty(w, "i");
                Val(w, "sz", f.Size.ToString("0.##", CultureInfo.InvariantCulture));
                if (f.Color is not null)
                {
                    w.WriteStartElement("color", Main);
                    w.WriteAttributeString("rgb", f.Color);
                    w.WriteEndElement();
                }
                Val(w, "name", f.Name);
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("fills", Main);
            w.WriteAttributeString("count", fills.Count.ToString(CultureInfo.InvariantCulture));
            for (var i = 0; i < fills.Count; i++)
            {
                w.WriteStartElement("fill", Main);
                w.WriteStartElement("patternFill", Main);
                w.WriteAttributeString("patternType", i == 0 ? "none" : i == 1 ? "gray125" : "solid");
                if (i >= 2)
                {
                    w.WriteStartElement("fgColor", Main);
                    w.WriteAttributeString("rgb", fills[i]);
                    w.WriteEndElement();
                    w.WriteStartElement("bgColor", Main);
                    w.WriteAttributeString("indexed", "64");
                    w.WriteEndElement();
                }
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("borders", Main);
            w.WriteAttributeString("count", "2");
            w.WriteStartElement("border", Main);
            foreach (var side in new[] { "left", "right", "top", "bottom", "diagonal" }) Empty(w, side);
            w.WriteEndElement();
            w.WriteStartElement("border", Main);
            foreach (var side in new[] { "left", "right", "top", "bottom" })
            {
                w.WriteStartElement(side, Main);
                w.WriteAttributeString("style", "thin");
                w.WriteStartElement("color", Main);
                w.WriteAttributeString("auto", "1");
                w.WriteEndElement();
                w.WriteEndElement();
            }
            Empty(w, "diagonal");
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("cellStyleXfs", Main);
            w.WriteAttributeString("count", "1");
            w.WriteStartElement("xf", Main);
            w.WriteAttributeString("numFmtId", "0");
            w.WriteAttributeString("fontId", "0");
            w.WriteAttributeString("fillId", "0");
            w.WriteAttributeString("borderId", "0");
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteStartElement("cellXfs", Main);
            w.WriteAttributeString("count", xfs.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var (font, fill, s) in xfs)
            {
                w.WriteStartElement("xf", Main);
                w.WriteAttributeString("numFmtId", "0");
                w.WriteAttributeString("fontId", font.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("fillId", fill.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("borderId", s.Border ? "1" : "0");
                w.WriteAttributeString("xfId", "0");
                w.WriteAttributeString("applyFont", "1");
                w.WriteAttributeString("applyFill", "1");
                w.WriteAttributeString("applyBorder", "1");
                w.WriteAttributeString("applyAlignment", "1");
                w.WriteStartElement("alignment", Main);
                w.WriteAttributeString("horizontal", s.Align switch { TextAlign.Center => "center", TextAlign.Right => "right", _ => "left" });
                w.WriteAttributeString("vertical", s.VAlign switch { VerticalAlign.Middle => "center", VerticalAlign.Bottom => "bottom", _ => "top" });
                if (s.Wrap) w.WriteAttributeString("wrapText", "1");
                w.WriteEndElement();
                w.WriteEndElement();
            }
            w.WriteEndElement();

            w.WriteStartElement("cellStyles", Main);
            w.WriteAttributeString("count", "1");
            w.WriteStartElement("cellStyle", Main);
            w.WriteAttributeString("name", "Normal");
            w.WriteAttributeString("xfId", "0");
            w.WriteAttributeString("builtinId", "0");
            w.WriteEndElement();
            w.WriteEndElement();

            w.WriteEndElement();

            static void Empty(XmlWriter w, string name)
            {
                w.WriteStartElement(name, Main);
                w.WriteEndElement();
            }

            static void Val(XmlWriter w, string name, string value)
            {
                w.WriteStartElement(name, Main);
                w.WriteAttributeString("val", value);
                w.WriteEndElement();
            }
        }
    }
}
