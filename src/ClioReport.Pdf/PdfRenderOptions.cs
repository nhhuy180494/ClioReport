namespace ClioReport.Pdf;

public sealed class PdfRenderOptions
{
    /// <summary>Font family name passed to PDFsharp. With the built-in resolver any name maps to <see cref="FontFilePath"/>.</summary>
    public string FontFamily { get; set; } = "Arial";

    /// <summary>Font size in points.</summary>
    public double FontSize { get; set; } = 10;

    /// <summary>
    /// TrueType file used when no <c>GlobalFontSettings.FontResolver</c> is registered yet. Must cover the characters
    /// in the report (e.g. Arial for Vietnamese). Defaults to arial.ttf in the system fonts folder.
    /// </summary>
    public string? FontFilePath { get; set; }

    /// <summary>Optional bold variant of <see cref="FontFilePath"/>. When null, bold is simulated.</summary>
    public string? BoldFontFilePath { get; set; }

    /// <summary>Optional italic variant of <see cref="FontFilePath"/>. When null, italic is simulated.</summary>
    public string? ItalicFontFilePath { get; set; }

    /// <summary>Optional bold italic variant of <see cref="FontFilePath"/>. When null, it is simulated.</summary>
    public string? BoldItalicFontFilePath { get; set; }

    /// <summary>
    /// Fallback TrueType file for characters <see cref="FontFilePath"/> does not have, e.g. Malgun Gothic (Hangul)
    /// behind Arial (Vietnamese). Used only when ClioReport registers its own font resolver.
    /// </summary>
    public string? FallbackFontFilePath { get; set; }

    /// <summary>Optional bold variant of <see cref="FallbackFontFilePath"/>. When null, bold is simulated.</summary>
    public string? FallbackBoldFontFilePath { get; set; }

    /// <summary>
    /// Folder that image elements may load files from when their value is a path (resolved against this folder;
    /// paths escaping it are rejected). Null disables path loading: supply images as byte[] instead.
    /// </summary>
    public string? ImageRoot { get; set; }
}
