using System.Diagnostics.CodeAnalysis;

namespace ClioReport.Pdf;

/// <summary>Where a font comes from: a TrueType file, or bytes produced on demand (e.g. an embedded resource).</summary>
public sealed class FontSource
{
    private readonly string? _path;
    private readonly Func<byte[]>? _loader;

    private FontSource(string? path, Func<byte[]>? loader)
    {
        _path = path;
        _loader = loader;
    }

    /// <summary>A TrueType file on disk.</summary>
    public static FontSource FromFile(string path) =>
        new(path ?? throw new ArgumentNullException(nameof(path)), null);

    /// <summary>A font held in memory. <paramref name="loader"/> runs at most once per render process.</summary>
    public static FontSource FromBytes(Func<byte[]> loader) =>
        new(null, loader ?? throw new ArgumentNullException(nameof(loader)));

    [return: NotNullIfNotNull(nameof(path))]
    public static implicit operator FontSource?(string? path) => path is null ? null : FromFile(path);

    internal byte[] Read() => _loader is not null ? _loader() : File.ReadAllBytes(_path!);

    /// <summary>Throws when this is a file that does not exist.</summary>
    internal void EnsureExists(string family)
    {
        if (_path is not null && !File.Exists(_path))
            throw new FileNotFoundException($"Font file of family '{family}' not found.", _path);
    }
}
