using System.Globalization;
using System.Text.RegularExpressions;

namespace ClioReport.Rdlc;

/// <summary>RDL lengths ("2.5cm", "10pt") and colours.</summary>
internal static class RdlcUnits
{
    private static readonly Regex LengthPattern = new(@"^\s*(-?\d+(?:\.\d+)?)\s*(mm|cm|in|pt|pc)?\s*$", RegexOptions.CultureInvariant);

    public static bool TryMillimetres(string? text, out double mm)
    {
        mm = 0;
        if (text is null) return false;
        var m = LengthPattern.Match(text);
        if (!m.Success) return false;

        var value = double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
        mm = m.Groups[2].Value switch
        {
            "cm" => value * 10,
            "in" => value * 25.4,
            "pt" => value * 25.4 / 72,
            "pc" => value * 25.4 / 6,
            _ => value
        };
        return true;
    }

    /// <summary>Length in points, for font sizes.</summary>
    public static bool TryPoints(string? text, out double pt)
    {
        pt = 0;
        if (!TryMillimetres(text, out var mm)) return false;
        pt = mm * 72 / 25.4;
        return true;
    }

    private static readonly Dictionary<string, string> Named = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Black"] = "#000000", ["White"] = "#FFFFFF", ["Red"] = "#FF0000", ["Green"] = "#008000",
        ["Blue"] = "#0000FF", ["Yellow"] = "#FFFF00", ["Gray"] = "#808080", ["Grey"] = "#808080",
        ["Silver"] = "#C0C0C0", ["LightGray"] = "#D3D3D3", ["LightGrey"] = "#D3D3D3", ["DarkGray"] = "#A9A9A9",
        ["Orange"] = "#FFA500", ["Maroon"] = "#800000", ["Navy"] = "#000080", ["Purple"] = "#800080",
        ["Teal"] = "#008080", ["Lime"] = "#00FF00", ["Aqua"] = "#00FFFF", ["Fuchsia"] = "#FF00FF",
        ["WhiteSmoke"] = "#F5F5F5", ["Gainsboro"] = "#DCDCDC", ["LightSteelBlue"] = "#B0C4DE"
    };

    private static readonly Regex Hex = new("^#([0-9a-fA-F]{3}|[0-9a-fA-F]{6})$", RegexOptions.CultureInvariant);

    /// <summary>Returns "#RRGGBB"/"#RGB" or null when the colour is not a plain value (expressions, unknown names).</summary>
    public static string? Color(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        if (Hex.IsMatch(text)) return text;
        return Named.TryGetValue(text, out var hex) ? hex : null;
    }
}
