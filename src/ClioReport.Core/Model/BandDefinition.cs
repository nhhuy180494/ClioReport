namespace ClioReport.Model;

public enum BandType
{
    ReportHeader,
    PageHeader,
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

    /// <summary>Wrap text to the element width. The band grows to fit; requires an ITextMeasurer.</summary>
    public bool Wrap { get; set; }

    /// <summary>Bold text. Without a bold font file the PDF renderer simulates it.</summary>
    public bool Bold { get; set; }

    /// <summary>Italic text. Without an italic font file the PDF renderer simulates it.</summary>
    public bool Italic { get; set; }

    /// <summary>Font size in points; 0 uses the renderer's default size.</summary>
    public double FontSize { get; set; }

    /// <summary>Static text.</summary>
    public string? Text { get; set; }

    /// <summary>Expression evaluated per row; takes precedence over <see cref="Text"/>.</summary>
    public string? Expression { get; set; }
}
