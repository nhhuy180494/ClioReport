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
}
