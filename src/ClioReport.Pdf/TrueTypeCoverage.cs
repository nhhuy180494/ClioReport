using System.Buffers.Binary;

namespace ClioReport.Pdf;

/// <summary>Reads which characters a TrueType/OpenType font file has glyphs for (its 'cmap' table).</summary>
internal sealed class TrueTypeCoverage
{
    private readonly Func<int, bool> _has;

    private TrueTypeCoverage(Func<int, bool> has) => _has = has;

    public bool Has(int codePoint) => _has(codePoint);

    /// <summary>Null when the file cannot be read or has no usable cmap; callers then treat every character as covered.</summary>
    public static TrueTypeCoverage? Load(string path)
    {
        try
        {
            return Parse(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    internal static TrueTypeCoverage? Parse(byte[] data)
    {
        try
        {
            var start = 0;
            if (Tag(data, 0) == "ttcf") start = (int)U32(data, 12); // first font of a collection

            var tables = U16(data, start + 4);
            var cmap = -1;
            for (var i = 0; i < tables; i++)
            {
                var record = start + 12 + i * 16;
                if (Tag(data, record) == "cmap") cmap = (int)U32(data, record + 8);
            }
            if (cmap < 0) return null;

            // Prefer full Unicode (format 12), then BMP (format 4).
            var count = U16(data, cmap + 2);
            int best12 = -1, best4 = -1;
            for (var i = 0; i < count; i++)
            {
                var platform = U16(data, cmap + 4 + i * 8);
                var encoding = U16(data, cmap + 6 + i * 8);
                var offset = cmap + (int)U32(data, cmap + 8 + i * 8);
                var unicode = platform == 0 || (platform == 3 && (encoding == 1 || encoding == 10));
                if (!unicode) continue;

                var format = U16(data, offset);
                if (format == 12) best12 = offset;
                else if (format == 4 && best4 < 0) best4 = offset;
            }

            if (best12 >= 0) return new TrueTypeCoverage(Format12(data, best12));
            if (best4 >= 0) return new TrueTypeCoverage(Format4(data, best4));
            return null;
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static Func<int, bool> Format12(byte[] data, int table)
    {
        var groups = (int)U32(data, table + 12);
        return code =>
        {
            int lo = 0, hi = groups - 1;
            while (lo <= hi)
            {
                var mid = (lo + hi) / 2;
                var g = table + 16 + mid * 12;
                if (code < U32(data, g)) hi = mid - 1;
                else if (code > U32(data, g + 4)) lo = mid + 1;
                else return true;
            }
            return false;
        };
    }

    private static Func<int, bool> Format4(byte[] data, int table)
    {
        var segCount = U16(data, table + 6) / 2;
        var endCodes = table + 14;
        var startCodes = endCodes + segCount * 2 + 2;
        var deltas = startCodes + segCount * 2;
        var rangeOffsets = deltas + segCount * 2;

        return code =>
        {
            if (code > 0xFFFF) return false;
            for (var i = 0; i < segCount; i++)
            {
                if (code > U16(data, endCodes + i * 2)) continue;
                if (code < U16(data, startCodes + i * 2)) return false;

                var rangeOffset = U16(data, rangeOffsets + i * 2);
                if (rangeOffset == 0) return ((code + U16(data, deltas + i * 2)) & 0xFFFF) != 0;

                var glyphAt = rangeOffsets + i * 2 + rangeOffset + 2 * (code - U16(data, startCodes + i * 2));
                return U16(data, glyphAt) != 0;
            }
            return false;
        };
    }

    private static string Tag(byte[] d, int o) => new(new[] { (char)d[o], (char)d[o + 1], (char)d[o + 2], (char)d[o + 3] });

    private static int U16(byte[] d, int o) => BinaryPrimitives.ReadUInt16BigEndian(d.AsSpan(o, 2));

    private static uint U32(byte[] d, int o) => BinaryPrimitives.ReadUInt32BigEndian(d.AsSpan(o, 4));
}
