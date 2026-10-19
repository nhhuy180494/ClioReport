using System.Reflection;

namespace ClioReport.Pdf;

/// <summary>Registers the fonts embedded in this assembly (Liberation Sans, Serif and Mono, and Nanum Gothic for Hangul; all SIL OFL 1.1).</summary>
public static class BundledFonts
{
    /// <summary>
    /// Registers the families "Arial", "Times New Roman" and "Courier New" (Liberation Sans, Serif and Mono, which have the
    /// same metrics) plus "Liberation Sans", "Liberation Serif" and "Liberation Mono". Latin, Vietnamese and Cyrillic are
    /// covered, and Hangul comes from the embedded Nanum Gothic as fallback (unless <see cref="PdfRenderOptions.FallbackFont"/> or
    /// <see cref="PdfRenderOptions.FallbackFontFilePath"/> is already set). Chinese and Japanese are not covered.
    /// Unless <see cref="PdfRenderOptions.FontFilePath"/> is set, <see cref="PdfRenderOptions.FontFamily"/> (default "Arial")
    /// picks the default font from these, so no font has to be installed. Call this before the first render, and use the
    /// same options for the text measurer and the renderer.
    /// </summary>
    public static PdfRenderOptions UseBundledFonts(this PdfRenderOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        Add(options, "Sans", "Arial", "Liberation Sans");
        Add(options, "Serif", "Times New Roman", "Liberation Serif");
        Add(options, "Mono", "Courier New", "Liberation Mono");

        // Hangul (Nanum Gothic) for characters the chosen font lacks, unless the caller set a fallback of their own.
        if (options.FallbackFont is null && options.FallbackFontFilePath is null)
        {
            options.FallbackFont = Embedded("NanumGothic");
            options.FallbackBoldFont = Embedded("NanumGothicBold");
        }
        return options;
    }

    private static void Add(PdfRenderOptions options, string face, params string[] names)
    {
        var fonts = new PdfFontFiles(Embedded(face, "Regular"))
        {
            Bold = Embedded(face, "Bold"),
            Italic = Embedded(face, "Italic"),
            BoldItalic = Embedded(face, "BoldItalic")
        };
        foreach (var name in names) options.Fonts[name] = fonts;
    }

    private static FontSource Embedded(string face, string style) => Embedded($"Liberation{face}-{style}");

    private static FontSource Embedded(string file)
    {
        var bytes = new Lazy<byte[]>(() => Read(file));
        return FontSource.FromBytes(() => bytes.Value);
    }

    private static byte[] Read(string file)
    {
        var name = $"ClioReport.Pdf.Fonts.{file}.ttf";
        using var stream = typeof(BundledFonts).Assembly.GetManifestResourceStream(name)
                           ?? throw new InvalidOperationException($"Embedded font '{name}' is missing.");
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }
}
