using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using ClioReport.Model;

namespace ClioReport.Xlsx;

/// <summary>
/// Writes the pages of a <see cref="RenderedReport"/> as one worksheet. Text elements become cells: the columns are a fixed grid (<see cref="XlsxRenderOptions.ColumnWidth"/>), rows follow the
/// top and bottom edges of the elements, and an element's cell is merged across the columns and rows it covers, so the sheet
/// keeps the proportions of the page. Pages follow each other with an
/// empty row between them. Images are placed over the cells they cover. Lines, rectangles, barcodes and QR codes are not exported. Plain numbers (such as
/// <c>1500.4</c>) become numeric cells; text that was formatted, for instance with <c>Format(x, 'N0')</c>, stays text.
/// </summary>
public static class XlsxReportRenderer
{
    private const double DefaultFontSize = 8;   // the PDF renderer's default size

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
        var pictures = sheet.Images.Where(p => ImageType(p.Data) is not null).ToList();
        Part(zip, "[Content_Types].xml", w => WriteContentTypes(w, pictures));
        Part(zip, "_rels/.rels", w => WriteRootRels(w));
        Part(zip, "xl/workbook.xml", w => WriteWorkbook(w, SheetName(options.SheetName)));
        Part(zip, "xl/_rels/workbook.xml.rels", w => WriteWorkbookRels(w));
        Part(zip, "xl/styles.xml", w => styles.Write(w));
        Part(zip, "xl/worksheets/sheet1.xml", w => WriteSheet(w, sheet, report, options, pictures.Count > 0));
        if (pictures.Count > 0)
        {
            Part(zip, "xl/worksheets/_rels/sheet1.xml.rels", w =>
            {
                w.WriteStartElement("Relationships", PackageRel);
                WriteRelationship(w, "rId1", RelNs + "/drawing", "../drawings/drawing1.xml");
                w.WriteEndElement();
            });
            Part(zip, "xl/drawings/drawing1.xml", w => WriteDrawing(w, pictures));
            Part(zip, "xl/drawings/_rels/drawing1.xml.rels", w =>
            {
                w.WriteStartElement("Relationships", PackageRel);
                for (var i = 0; i < pictures.Count; i++)
                    WriteRelationship(w, "rId" + (i + 1), RelNs + "/image", "../media/image" + (i + 1) + "." + ImageType(pictures[i].Data));
                w.WriteEndElement();
            });
            for (var i = 0; i < pictures.Count; i++)
            {
                using var media = zip.CreateEntry("xl/media/image" + (i + 1) + "." + ImageType(pictures[i].Data), CompressionLevel.NoCompression).Open();
                media.Write(pictures[i].Data);
            }
        }
    }

    // ---------------------------------------------------------------- layout -> grid

    private sealed class Cell
    {
        public int Row, EndRow, Col, EndCol;   // 1-based first row, zero-based first column; the End values are exclusive
        public string Text = "";
        public CellStyle Style;
        public int StyleIndex;
    }

    private sealed record Picture(int Row, int EndRow, int Col, int EndCol, byte[] Data);

    private sealed class Sheet
    {
        public List<Cell> Cells = new();
        public Dictionary<int, double> RowHeightsPt = new();
        public double[] ColumnWidthsMm = Array.Empty<double>();
        public int RowCount;
        public List<Picture> Images = new();
        public List<int> PageEndRows = new();   // last sheet row of every report page (PrintPages)
        public double LeftMm, TopMm;           // where the content starts on the report page (PrintPages margins)
    }

    private static Sheet BuildSheet(RenderedReport report, XlsxRenderOptions options)
    {
        // Pages go down the sheet in list order, not by Number: a report put together from several renders (each numbered
        // from 1) keeps every page apart.
        var items = new List<(int Page, RenderedElement E)>();
        for (var p = 0; p < report.Pages.Count; p++)
            foreach (var e in report.Pages[p].Elements)
                if (e.Kind == ElementKind.Image ? e.Image is { Length: > 0 } : e.Kind == ElementKind.Text && (!string.IsNullOrWhiteSpace(e.Text) || e.Border || e.Fill is not null))   // empty boxes keep their borders and fill
                    items.Add((p, e));

        var sheet = new Sheet();
        if (items.Count == 0) return sheet;

        // Columns: a fixed grid. Every element starts and ends on the nearest grid line and its cell is merged across the
        // columns it covers, so all columns have the same width and the sheet keeps the proportions of the page.
        var step = options.ColumnWidth > 0.5 ? options.ColumnWidth : 2.5;
        var originX = items.Min(i => i.E.X);
        int ColOf(double x) => (int)Math.Round((x - originX) / step, MidpointRounding.AwayFromZero);
        var columnCount = 1;

        // Rows: per page, every top and bottom edge is a boundary (edges closer than the tolerance count as one); a row is
        // the gap between two boundaries and an element's cell is merged down the rows it covers. With PrintPages every page
        // ends with a page break; without it a blank row separates pages.
        sheet.LeftMm = originX;
        sheet.TopMm = double.MaxValue;
        var rowBase = 0;
        foreach (var pageItems in items.GroupBy(i => i.Page).OrderBy(g => g.Key))
        {
            var list = pageItems.Select(i => i.E).ToList();
            var bounds = new List<double>();
            foreach (var y in list.SelectMany(e => new[] { e.Y, e.Y + Math.Max(e.Height, 1) }).OrderBy(y => y))
                if (bounds.Count == 0 || y - bounds[^1] > options.RowTolerance) bounds.Add(y);
            sheet.TopMm = Math.Min(sheet.TopMm, bounds[0]);
            int RowOf(double y)
            {
                var best = 0;
                for (var k = 1; k < bounds.Count; k++)
                    if (Math.Abs(bounds[k] - y) < Math.Abs(bounds[best] - y)) best = k;
                return best;
            }

            var placed = new List<Cell>();
            foreach (var e in list.OrderBy(e => e.Y).ThenBy(e => e.X))
            {
                var r0 = RowOf(e.Y);
                var r1 = Math.Max(RowOf(e.Y + Math.Max(e.Height, 1)), r0 + 1);
                var c0 = ColOf(e.X);
                var c1 = Math.Max(ColOf(e.X + Math.Max(e.Width, 1)), c0 + 1);
                columnCount = Math.Max(columnCount, c1);
                if (e.Kind == ElementKind.Image)
                {
                    sheet.Images.Add(new Picture(r0 + rowBase + 1, r1 + rowBase + 1, c0, c1, e.Image!));
                    continue;
                }
                Place(placed, new Cell { Row = r0 + rowBase + 1, EndRow = r1 + rowBase + 1, Col = c0, EndCol = c1, Text = e.Text, Style = CellStyle.From(e) });
            }
            sheet.Cells.AddRange(placed);

            var rows = Math.Max(bounds.Count - 1, 1);
            for (var k = 0; k < rows; k++)
                sheet.RowHeightsPt[rowBase + 1 + k] = k + 1 < bounds.Count ? Math.Max((bounds[k + 1] - bounds[k]) * 72 / 25.4, 1) : 15;
            rowBase += rows;
            sheet.RowCount = rowBase;
            if (options.PrintPages) sheet.PageEndRows.Add(rowBase);
            else rowBase++;
        }

        sheet.ColumnWidthsMm = Enumerable.Repeat(step, columnCount).ToArray();
        return sheet;
    }

    private static bool Overlap(Cell a, Cell b) => a.Row < b.EndRow && b.Row < a.EndRow && a.Col < b.EndCol && b.Col < a.EndCol;

    // Adds a cell; where it overlaps another one, one of the two gives way so no merged ranges intersect.
    private static void Place(List<Cell> list, Cell n)
    {
        for (var pass = 0; pass < 2 * list.Count + 4; pass++)
        {
            var hit = list.FirstOrDefault(o => Overlap(o, n));
            if (hit is null) { list.Add(n); return; }
            if (hit.Row == n.Row && hit.Col == n.Col) { hit.Text = (hit.Text + " " + n.Text).Trim(); return; }
            if (n.Col < hit.Col) n.EndCol = hit.Col;
            else if (n.Row < hit.Row) n.EndRow = hit.Row;
            else if (hit.Col < n.Col) hit.EndCol = n.Col;
            else if (hit.Row < n.Row) hit.EndRow = n.Row;
            else { hit.Text = (hit.Text + " " + n.Text).Trim(); return; }
        }
        var last = list.FirstOrDefault(o => Overlap(o, n));
        if (last is null) list.Add(n); else last.Text = (last.Text + " " + n.Text).Trim();
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

    private static void WriteContentTypes(XmlWriter w, List<Picture> pictures)
    {
        w.WriteStartElement("Types", ContentTypes);
        Default(w, "rels", "application/vnd.openxmlformats-package.relationships+xml");
        Default(w, "xml", "application/xml");
        foreach (var ext in pictures.Select(p => ImageType(p.Data)!).Distinct())
            Default(w, ext, ext switch { "png" => "image/png", "jpeg" => "image/jpeg", "gif" => "image/gif", _ => "image/bmp" });
        if (pictures.Count > 0)
            Override(w, "/xl/drawings/drawing1.xml", "application/vnd.openxmlformats-officedocument.drawing+xml");
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

    private static void WriteSheet(XmlWriter w, Sheet sheet, RenderedReport report, XlsxRenderOptions options, bool hasDrawing)
    {
        var landscape = report.PageWidth > report.PageHeight;
        var showGridLines = options.ShowGridLines;
        w.WriteStartElement("worksheet", Main);

        w.WriteStartElement("sheetPr", Main);                       // print: fit the width on one page
        w.WriteStartElement("pageSetUpPr", Main);
        w.WriteAttributeString("fitToPage", "1");
        w.WriteEndElement();
        w.WriteEndElement();

        w.WriteStartElement("sheetViews", Main);
        w.WriteStartElement("sheetView", Main);
        if (!showGridLines) w.WriteAttributeString("showGridLines", "0");
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
                w.WriteAttributeString("width", Math.Clamp(sheet.ColumnWidthsMm[i] / 1.7, 0.3, 100).ToString("0.##", CultureInfo.InvariantCulture));
                w.WriteAttributeString("customWidth", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        // every position of a merged range is written, so its borders and fill show on all sides
        var grid = new Dictionary<int, SortedDictionary<int, (Cell Cell, bool Main)>>();
        foreach (var cell in sheet.Cells)
            for (var r = cell.Row; r < cell.EndRow; r++)
                for (var c = cell.Col; c < cell.EndCol; c++)
                {
                    if (!grid.TryGetValue(r, out var line)) grid[r] = line = new SortedDictionary<int, (Cell, bool)>();
                    line[c] = (cell, r == cell.Row && c == cell.Col);
                }

        w.WriteStartElement("sheetData", Main);
        for (var r = 1; r <= sheet.RowCount; r++)
        {
            w.WriteStartElement("row", Main);
            w.WriteAttributeString("r", r.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("ht", Math.Clamp(sheet.RowHeightsPt.GetValueOrDefault(r, 15), 1, 409).ToString("0.##", CultureInfo.InvariantCulture));
            w.WriteAttributeString("customHeight", "1");
            if (grid.TryGetValue(r, out var line))
                foreach (var (col, entry) in line)
                    WriteCell(w, entry.Cell, r, col, entry.Main);
            w.WriteEndElement();
        }
        w.WriteEndElement();

        var merged = sheet.Cells.Where(c => c.EndCol - c.Col > 1 || c.EndRow - c.Row > 1).ToList();
        if (merged.Count > 0)
        {
            w.WriteStartElement("mergeCells", Main);
            w.WriteAttributeString("count", merged.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var c in merged)
            {
                w.WriteStartElement("mergeCell", Main);
                w.WriteAttributeString("ref", ColumnName(c.Col) + c.Row.ToString(CultureInfo.InvariantCulture) + ":" + ColumnName(c.EndCol - 1) + (c.EndRow - 1).ToString(CultureInfo.InvariantCulture));
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        // PrintPages: margins (inches) put the content where it sits on the report page; no header or footer space.
        var printPages = options.PrintPages && sheet.PageEndRows.Count > 0;
        static string Inch(double mm) => Math.Max(mm / 25.4, 0).ToString("0.####", CultureInfo.InvariantCulture);
        w.WriteStartElement("pageMargins", Main);
        w.WriteAttributeString("left", printPages ? Inch(sheet.LeftMm) : "0.5");
        w.WriteAttributeString("right", printPages ? "0" : "0.5");
        w.WriteAttributeString("top", printPages ? Inch(sheet.TopMm) : "0.5");
        w.WriteAttributeString("bottom", printPages ? "0" : "0.5");
        w.WriteAttributeString("header", printPages ? "0" : "0.3");
        w.WriteAttributeString("footer", printPages ? "0" : "0.3");
        w.WriteEndElement();

        // PrintPages: the paper is the report page (paperWidth/paperHeight; paperSize A4 stays as the fallback for readers
        // that only know paperSize). Width and height are given for the portrait sheet, orientation turns it.
        static string Mm(double mm) => mm.ToString("0.##", CultureInfo.InvariantCulture) + "mm";
        w.WriteStartElement("pageSetup", Main);
        w.WriteAttributeString("paperSize", "9");
        if (printPages)
        {
            w.WriteAttributeString("paperHeight", Mm(Math.Max(report.PageWidth, report.PageHeight)));
            w.WriteAttributeString("paperWidth", Mm(Math.Min(report.PageWidth, report.PageHeight)));
        }
        w.WriteAttributeString("orientation", landscape ? "landscape" : "portrait");
        w.WriteAttributeString("fitToWidth", "1");
        w.WriteAttributeString("fitToHeight", "0");
        w.WriteEndElement();

        // PrintPages: a manual break after every page except the last.
        if (printPages && sheet.PageEndRows.Count > 1)
        {
            var breaks = sheet.PageEndRows.Take(sheet.PageEndRows.Count - 1).ToList();
            w.WriteStartElement("rowBreaks", Main);
            w.WriteAttributeString("count", breaks.Count.ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("manualBreakCount", breaks.Count.ToString(CultureInfo.InvariantCulture));
            foreach (var row in breaks)
            {
                w.WriteStartElement("brk", Main);
                w.WriteAttributeString("id", row.ToString(CultureInfo.InvariantCulture));
                w.WriteAttributeString("max", "16383");
                w.WriteAttributeString("man", "1");
                w.WriteEndElement();
            }
            w.WriteEndElement();
        }

        if (hasDrawing)
        {
            w.WriteStartElement("drawing", Main);
            w.WriteAttributeString("r", "id", RelNs, "rId1");
            w.WriteEndElement();
        }

        w.WriteEndElement();
    }

    private static void WriteCell(XmlWriter w, Cell cell, int row, int col, bool main)
    {
        var text = main ? Clean(cell.Text) : "";
        w.WriteStartElement("c", Main);
        w.WriteAttributeString("r", ColumnName(col) + row.ToString(CultureInfo.InvariantCulture));
        w.WriteAttributeString("s", cell.StyleIndex.ToString(CultureInfo.InvariantCulture));
        if (text.Length == 0)
        {
            // empty cell: only the style (border, fill) is kept
        }
        else if (PlainNumber.IsMatch(text))
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

    private static string? ImageType(byte[] d) =>
        d.Length > 8 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47 ? "png" :
        d.Length > 3 && d[0] == 0xFF && d[1] == 0xD8 ? "jpeg" :
        d.Length > 3 && d[0] == 0x47 && d[1] == 0x49 && d[2] == 0x46 ? "gif" :
        d.Length > 2 && d[0] == 0x42 && d[1] == 0x4D ? "bmp" : null;

    private static void WriteDrawing(XmlWriter w, List<Picture> pictures)
    {
        const string Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
        const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        w.WriteStartElement("xdr", "wsDr", Xdr);
        w.WriteAttributeString("xmlns", "a", null, A);
        w.WriteAttributeString("xmlns", "r", null, RelNs);
        for (var i = 0; i < pictures.Count; i++)
        {
            var p = pictures[i];
            w.WriteStartElement("xdr", "twoCellAnchor", Xdr);
            w.WriteAttributeString("editAs", "oneCell");
            Corner(w, "from", p.Col, p.Row - 1);
            Corner(w, "to", p.EndCol, p.EndRow - 1);
            w.WriteStartElement("xdr", "pic", Xdr);
            w.WriteStartElement("xdr", "nvPicPr", Xdr);
            w.WriteStartElement("xdr", "cNvPr", Xdr);
            w.WriteAttributeString("id", (i + 2).ToString(CultureInfo.InvariantCulture));
            w.WriteAttributeString("name", "Picture " + (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteElementString("xdr", "cNvPicPr", Xdr, "");
            w.WriteEndElement();
            w.WriteStartElement("xdr", "blipFill", Xdr);
            w.WriteStartElement("a", "blip", A);
            w.WriteAttributeString("r", "embed", RelNs, "rId" + (i + 1).ToString(CultureInfo.InvariantCulture));
            w.WriteEndElement();
            w.WriteStartElement("a", "stretch", A);
            w.WriteElementString("a", "fillRect", A, "");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteStartElement("xdr", "spPr", Xdr);
            w.WriteStartElement("a", "prstGeom", A);
            w.WriteAttributeString("prst", "rect");
            w.WriteElementString("a", "avLst", A, "");
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteEndElement();
            w.WriteElementString("xdr", "clientData", Xdr, "");
            w.WriteEndElement();
        }
        w.WriteEndElement();

        static void Corner(XmlWriter w, string name, int col, int row)
        {
            w.WriteStartElement("xdr", name, Xdr);
            w.WriteElementString("xdr", "col", Xdr, col.ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("xdr", "colOff", Xdr, "0");
            w.WriteElementString("xdr", "row", Xdr, row.ToString(CultureInfo.InvariantCulture));
            w.WriteElementString("xdr", "rowOff", Xdr, "0");
            w.WriteEndElement();
        }
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
                var f = (s.Bold, s.Italic, s.FontSize > 0 ? s.FontSize : DefaultFontSize, "Arial", s.Color);
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
