namespace ClioReport.Model;

public enum BandType
{
    ReportHeader,
    PageHeader,
    ColumnHeader,
    GroupHeader,
    Detail,
    GroupFooter,
    PageFooter,
    ReportFooter
}

public enum TextAlign
{
    Left,
    Center,
    Right
}

public enum VerticalAlign
{
    Top,
    Middle,
    Bottom
}

public sealed class BandDefinition
{
    public BandType Type { get; set; }

    /// <summary>Band height in millimetres.</summary>
    public double Height { get; set; }

    /// <summary>Group name for GroupHeader/GroupFooter bands.</summary>
    public string? Group { get; set; }

    /// <summary>
    /// Optional expression; the band is skipped when it is falsy (false, 0, empty text, null). For page header/footer
    /// bands the space stays reserved and only the content is skipped.
    /// </summary>
    public string? Visible { get; set; }

    public List<ElementDefinition> Elements { get; set; } = new();
}

public sealed class ElementDefinition
{
    /// <summary>Element kind: "text" (default), "line", "rect", "barcode" (Code 128), "qrcode" or "image".</summary>
    public string Type { get; set; } = "text";

    // Position/size relative to the band, in millimetres.
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }

    /// <summary>Stroke width in millimetres for "line" and "rect".</summary>
    public double Thickness { get; set; } = 0.2;

    /// <summary>Horizontal alignment inside the element box.</summary>
    public TextAlign Align { get; set; } = TextAlign.Left;

    /// <summary>Vertical position of the text inside the element box (default top).</summary>
    public VerticalAlign VAlign { get; set; }

    /// <summary>
    /// Stretches the element to the bottom of its band, so fill and border match a band that grew because of wrapped text.
    /// </summary>
    public bool FitBand { get; set; }

    /// <summary>Wrap text to the element width. The band grows to fit; requires an ITextMeasurer.</summary>
    public bool Wrap { get; set; }

    /// <summary>Bold text. Without a bold font file the PDF renderer simulates it.</summary>
    public bool Bold { get; set; }

    /// <summary>Italic text. Without an italic font file the PDF renderer simulates it.</summary>
    public bool Italic { get; set; }

    /// <summary>Underlined text.</summary>
    public bool Underline { get; set; }

    /// <summary>Rotates a text element's content: 0 (default), 90 (clockwise) or 270 (reads bottom to top). Rotated text is not wrapped.</summary>
    public int Rotate { get; set; }

    /// <summary>Font size in points; 0 uses the renderer's default size.</summary>
    public double FontSize { get; set; }

    /// <summary>Text, line and outline colour as "#RGB" or "#RRGGBB". Default black.</summary>
    public string? Color { get; set; }

    /// <summary>Background colour of the element box ("#RGB" or "#RRGGBB"), for text and rect elements.</summary>
    public string? Fill { get; set; }

    /// <summary>Draws an outline (using <see cref="Thickness"/>) around a text element's box.</summary>
    public bool Border { get; set; }

    /// <summary>Horizontal padding in millimetres inside a text element's box (both sides). Wrapping uses the narrower width.</summary>
    public double Padding { get; set; }

    /// <summary>Static text.</summary>
    public string? Text { get; set; }

    // ---- "table" and "list" elements -----------------------------------------------------------------------

    /// <summary>
    /// "table": the columns. Widths are in millimetres; columns with width 0 share what is left of the element
    /// width. The table is as tall as its rows and grows its band; it cannot be split across pages.
    /// </summary>
    public List<ColumnDefinition>? Columns { get; set; }

    /// <summary>
    /// "table" and "list": name of the collection to repeat over, resolved with <c>GetRows</c> on the current row (so a
    /// detail band can list the children of its row) or on the report root elsewhere.
    /// </summary>
    public string? Source { get; set; }

    /// <summary>"table": fixed rows instead of <see cref="Source"/>. Each cell is text, or an expression when it starts with "=".</summary>
    public List<List<string>>? Rows { get; set; }

    /// <summary>"table": height of a data row in millimetres (default 5). Rows grow when <see cref="Wrap"/> is on.</summary>
    public double RowHeight { get; set; }

    /// <summary>"table": height of the header row; 0 uses <see cref="RowHeight"/>.</summary>
    public double HeaderHeight { get; set; }

    /// <summary>"table": print the header row (column titles, bold, filled with <see cref="Fill"/>). Default true.</summary>
    public bool ShowHeader { get; set; } = true;

    /// <summary>"table": background colour of every second data row.</summary>
    public string? AltFill { get; set; }

    /// <summary>"list": elements of one item, positioned relative to the item's top-left corner. They see the item's fields.</summary>
    public List<ElementDefinition>? Items { get; set; }

    /// <summary>"list": height of one item in millimetres. Items have a fixed height; elements inside do not grow it.</summary>
    public double ItemHeight { get; set; }

    /// <summary>"list": items per row (default 1); item width is the element width shared by this many items.</summary>
    public int Across { get; set; } = 1;

    /// <summary>"list": space between items, horizontally and vertically, in millimetres.</summary>
    public double Gap { get; set; }

    internal ElementDefinition WithOffset(double dx, double dy)
    {
        var copy = (ElementDefinition)MemberwiseClone();
        copy.X += dx;
        copy.Y += dy;
        return copy;
    }

    /// <summary>Expression evaluated per row; takes precedence over <see cref="Text"/>.</summary>
    public string? Expression { get; set; }
}

/// <summary>A column of a "table" element.</summary>
public sealed class ColumnDefinition
{
    /// <summary>Title printed in the header row.</summary>
    public string Header { get; set; } = "";

    /// <summary>Expression evaluated per row (fields of the row). Takes precedence over <see cref="Text"/>.</summary>
    public string? Expression { get; set; }

    /// <summary>Static text, used when there is no expression.</summary>
    public string? Text { get; set; }

    /// <summary>Column width in millimetres; 0 shares the remaining width.</summary>
    public double Width { get; set; }

    public TextAlign Align { get; set; }
}
