using PdfSharp.Drawing;

namespace ClioReport.Pdf;

/// <summary>Loads each image once per render and keeps it alive until the document is saved.</summary>
internal sealed class ImageCache : IDisposable
{
    private readonly PdfRenderOptions _options;
    private readonly Dictionary<byte[], XImage> _byBytes = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, XImage> _byPath = new(StringComparer.Ordinal);
    private readonly List<IDisposable> _owned = new();

    public ImageCache(PdfRenderOptions options) => _options = options;

    public XImage? Get(RenderedElement element)
    {
        if (element.Image is { Length: > 0 } bytes)
        {
            if (_byBytes.TryGetValue(bytes, out var cached)) return cached;

            var stream = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: true);
            var image = XImage.FromStream(stream);
            _owned.Add(stream);
            _owned.Add(image);
            _byBytes[bytes] = image;
            return image;
        }

        if (element.Text.Length == 0) return null;

        // A file that is not there is "no image" (the renderer draws a placeholder); a path that is not allowed still throws.
        string path;
        try { path = ResolvePath(element.Text); }
        catch (FileNotFoundException) { return null; }
        if (_byPath.TryGetValue(path, out var known)) return known;

        var fromFile = XImage.FromFile(path);
        _owned.Add(fromFile);
        _byPath[path] = fromFile;
        return fromFile;
    }

    private string ResolvePath(string requested)
    {
        if (_options.ImageRoot is null)
            throw new InvalidOperationException(
                "Image paths are disabled. Set PdfRenderOptions.ImageRoot to the folder images may be loaded from, " +
                "or return the image as byte[] from the expression.");

        var root = Path.GetFullPath(_options.ImageRoot);
        var full = Path.GetFullPath(Path.Combine(root, requested));
        var relative = Path.GetRelativePath(root, full);
        if (relative.StartsWith("..", StringComparison.Ordinal) || Path.IsPathRooted(relative))
            throw new InvalidOperationException($"Image path '{requested}' is outside PdfRenderOptions.ImageRoot.");
        if (!File.Exists(full))
            throw new FileNotFoundException($"Image not found: {requested}", full);
        return full;
    }

    public void Dispose()
    {
        for (var i = _owned.Count - 1; i >= 0; i--)
            _owned[i].Dispose();
    }
}
