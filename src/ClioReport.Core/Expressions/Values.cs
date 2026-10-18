using System.Globalization;

namespace ClioReport.Expressions;

internal static class Values
{
    /// <summary>Numeric field values become decimal so the evaluator only deals with one number type.</summary>
    public static object? Normalize(object? v) => v switch
    {
        null => null,
        decimal or string or bool or DateTime => v,
        sbyte or byte or short or ushort or int or uint or long or ulong or float or double
            => Convert.ToDecimal(v, CultureInfo.InvariantCulture),
        _ => v
    };

    public static string Describe(object? v) => v switch
    {
        null => "null",
        decimal => "number",
        string => "string",
        bool => "boolean",
        _ => v.GetType().Name
    };

    public static decimal Num(object? v, string what) =>
        v is decimal d ? d : throw new ExpressionException($"'{what}' expects a number but got {Describe(v)}.");

    public static string Str(object? v) => v switch
    {
        null => "",
        string s => s,
        bool b => b ? "true" : "false",
        IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
        _ => v.ToString() ?? ""
    };

    /// <summary>Only bool is allowed as a condition; null counts as false.</summary>
    public static bool Truthy(object? v) => v switch
    {
        bool b => b,
        null => false,
        _ => throw new ExpressionException($"Expected a boolean but got {Describe(v)}.")
    };

    public static bool Eq(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        return a.GetType() == b.GetType() && a.Equals(b);
    }

    public static int Cmp(object a, object b)
    {
        if (a is decimal x && b is decimal y) return x.CompareTo(y);
        if (a is string s && b is string t) return string.CompareOrdinal(s, t);
        if (a is DateTime d && b is DateTime e) return d.CompareTo(e);
        throw new ExpressionException($"Cannot compare {Describe(a)} with {Describe(b)}.");
    }

    public static object? Arith(string op, object? l, object? r)
    {
        if (op == "+" && (l is string || r is string)) return Str(l) + Str(r);
        if (l is null || r is null) return null;

        var a = Num(l, op);
        var b = Num(r, op);
        switch (op)
        {
            case "+": return a + b;
            case "-": return a - b;
            case "*": return a * b;
            case "/":
                if (b == 0) throw new ExpressionException("Division by zero.");
                return a / b;
            default:
                if (b == 0) throw new ExpressionException("Division by zero.");
                return a % b;
        }
    }
}
