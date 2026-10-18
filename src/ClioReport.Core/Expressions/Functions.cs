using System.Globalization;

namespace ClioReport.Expressions;

internal sealed record FunctionInfo(int MinArgs, int MaxArgs, Func<object?[], object?> Impl);

/// <summary>The only functions an expression can call. Add new ones here explicitly.</summary>
internal static class Functions
{
    private static readonly Dictionary<string, FunctionInfo> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Round"] = new(1, 2, Round),
        ["Abs"] = new(1, 1, Abs),
        ["Min"] = new(2, 2, a => MinMax(a, "Min", false)),
        ["Max"] = new(2, 2, a => MinMax(a, "Max", true)),
        ["Upper"] = new(1, 1, a => Values.Str(a[0]).ToUpperInvariant()),
        ["Lower"] = new(1, 1, a => Values.Str(a[0]).ToLowerInvariant()),
        ["Trim"] = new(1, 1, a => Values.Str(a[0]).Trim()),
        ["Len"] = new(1, 1, a => (decimal)Values.Str(a[0]).Length),
        ["IsNull"] = new(2, 2, a => a[0] ?? a[1]),
        ["Format"] = new(2, 2, Format),
    };

    public static bool TryGet(string name, out FunctionInfo fn) => Table.TryGetValue(name, out fn!);

    private static object? Round(object?[] a)
    {
        if (a[0] is null) return null;
        var digits = a.Length > 1 ? Values.Num(a[1], "Round") : 0m;
        if (digits < 0 || digits > 28 || digits != decimal.Truncate(digits))
            throw new ExpressionException("Round: digits must be an integer between 0 and 28.");
        return Math.Round(Values.Num(a[0], "Round"), (int)digits, MidpointRounding.AwayFromZero);
    }

    private static object? Abs(object?[] a) =>
        a[0] is null ? null : Math.Abs(Values.Num(a[0], "Abs"));

    private static object? MinMax(object?[] a, string name, bool max)
    {
        if (a[0] is null || a[1] is null) return null;
        var x = Values.Num(a[0], name);
        var y = Values.Num(a[1], name);
        return max ? Math.Max(x, y) : Math.Min(x, y);
    }

    private static object? Format(object?[] a)
    {
        if (a[0] is null) return "";
        var fmt = Values.Str(a[1]);
        return a[0] is IFormattable f ? f.ToString(fmt, CultureInfo.InvariantCulture) : Values.Str(a[0]);
    }
}
