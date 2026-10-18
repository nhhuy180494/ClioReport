namespace ClioReport;

/// <summary>
/// Code 128 encoder with no dependencies. Uses set C for digit-only text of even length (4 or more digits) and set B
/// (printable ASCII, 32 to 126) otherwise.
/// </summary>
public static class Code128
{
    private const int StartB = 104;
    private const int StartC = 105;
    private const int Stop = 106;

    /// <summary>Bar/space widths (in modules) per symbol value, starting with a bar. Index 106 is the stop pattern.</summary>
    private static readonly string[] Patterns =
    {
        "212222", "222122", "222221", "121223", "121322", "131222", "122213", "122312",
        "132212", "221213", "221312", "231212", "112232", "122132", "122231", "113222",
        "123122", "123221", "223211", "221132", "221231", "213212", "223112", "312131",
        "311222", "321122", "321221", "312212", "322112", "322211", "212123", "212321",
        "232121", "111323", "131123", "131321", "112313", "132113", "132311", "211313",
        "231113", "231311", "112133", "112331", "132131", "113123", "113321", "133121",
        "313121", "211331", "231131", "213113", "213311", "213131", "311123", "311321",
        "331121", "312113", "312311", "332111", "314111", "221411", "431111", "111224",
        "111422", "121124", "121421", "141122", "141221", "112214", "112412", "122114",
        "122411", "142112", "142211", "241211", "221114", "413111", "241112", "134111",
        "111242", "121142", "121241", "114212", "124112", "124211", "411212", "421112",
        "421211", "212141", "214121", "412121", "111143", "111341", "131141", "114113",
        "114311", "411113", "411311", "113141", "114131", "311141", "411131", "211412",
        "211214", "211232", "2331112",
    };

    /// <summary>
    /// Returns the symbol as modules (true = bar), including start, checksum and stop but no quiet zones.
    /// Throws <see cref="ArgumentException"/> for empty text or characters set B cannot encode.
    /// </summary>
    public static bool[] Encode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length == 0)
            throw new ArgumentException("Code 128 needs at least one character.", nameof(text));

        var values = new List<int>(text.Length + 3);
        if (text.Length >= 4 && text.Length % 2 == 0 && text.All(char.IsAsciiDigit))
        {
            values.Add(StartC);
            for (var i = 0; i < text.Length; i += 2)
                values.Add((text[i] - '0') * 10 + (text[i + 1] - '0'));
        }
        else
        {
            values.Add(StartB);
            foreach (var c in text)
            {
                if (c < 32 || c > 126)
                    throw new ArgumentException($"Code 128 cannot encode character U+{(int)c:X4}.", nameof(text));
                values.Add(c - 32);
            }
        }

        var checksum = values[0];
        for (var i = 1; i < values.Count; i++)
            checksum += values[i] * i;
        values.Add(checksum % 103);
        values.Add(Stop);

        var modules = new List<bool>(values.Count * 11 + 2);
        foreach (var value in values)
        {
            var bar = true;
            foreach (var width in Patterns[value])
            {
                for (var k = 0; k < width - '0'; k++)
                    modules.Add(bar);
                bar = !bar;
            }
        }
        return modules.ToArray();
    }
}
