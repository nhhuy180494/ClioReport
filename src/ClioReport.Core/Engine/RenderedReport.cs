using ClioReport.Model;

namespace ClioReport;

/// <summary>Layout result: positioned text on pages (millimetres). Renderers such as PDF consume this.</summary>
public sealed record RenderedReport(double PageWidth, double PageHeight, IReadOnlyList<RenderedPage> Pages);

public sealed class RenderedPage
{
    private readonly List<RenderedElement> _elements = new();

    internal RenderedPage(int number) => Number = number;

    /// <summary>1-based page number.</summary>
    public int Number { get; }

    public IReadOnlyList<RenderedElement> Elements => _elements;

    internal void Add(RenderedElement element) => _elements.Add(element);
}

/// <summary>
/// Absolute position on the page, in millimetres from the top-left corner. When <c>LineHeight</c> is above zero the
/// text was wrapped: lines are separated by '\n' and each line is <c>LineHeight</c> tall.
/// </summary>
public enum ElementKind
{
    Text,
    /// <summary>Straight line from (X, Y) to (X + Width, Y + Height); Height 0 gives a horizontal rule.</summary>
    Line,
    /// <summary>Outlined rectangle.</summary>
    Rect,
    /// <summary>Code 128 barcode of <c>Text</c>, scaled to fill the element width.</summary>
    Barcode,
    QrCode,
    /// <summary>Image from <c>Image</c> bytes, or from the file path in <c>Text</c>; fitted inside the box.</summary>
    Image
}

public sealed record RenderedElement(
    double X, double Y, double Width, double Height, string Text,
    TextAlign Align = TextAlign.Left, ElementKind Kind = ElementKind.Text, double Thickness = 0.2,
    double LineHeight = 0, byte[]? Image = null);
