namespace ClioReport;

/// <summary>
/// Font variation of a text element. <c>FontSize</c> is in points; 0 means the renderer's default size.
/// <c>FontFamily</c> names a font the renderer knows; null or an unknown name means the renderer's default font.
/// </summary>
public readonly record struct TextStyle(bool Bold = false, bool Italic = false, double FontSize = 0, string? FontFamily = null);
