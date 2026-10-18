namespace ClioReport;

/// <summary>Font variation of a text element. <c>FontSize</c> is in points; 0 means the renderer's default size.</summary>
public readonly record struct TextStyle(bool Bold = false, bool Italic = false, double FontSize = 0);
