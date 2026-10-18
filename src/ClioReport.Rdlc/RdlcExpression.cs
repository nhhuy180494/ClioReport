using System.Text;
using System.Text.RegularExpressions;
using ClioReport.Model;

namespace ClioReport.Rdlc;

/// <summary>
/// Translates the common subset of RDL (Visual Basic) expressions into ClioReport expressions: field, parameter and
/// page-number references, string concatenation with &amp;, comparison and logic operators, Sum/Count/Avg/Min/Max
/// (turned into aggregates) and a few string functions. Anything else is reported, not guessed.
/// </summary>
internal sealed class RdlcExpression(List<AggregateDefinition> aggregates, HashSet<string> groups)
{
    private static readonly Regex Literal = new("\"(?:[^\"]|\"\")*\"", RegexOptions.CultureInvariant);
    private static readonly Regex Aggregate = new(@"\b(Sum|Count|Avg|Min|Max|CountDistinct|CountRows|First|Last|StDev|Var)\s*\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex Call = new(@"\b([A-Za-z_]\w*)\s*\(", RegexOptions.CultureInvariant);
    private static readonly Regex Reference = new(@"\b(Fields|Globals|Parameters|User|ReportItems|DataSets|DataSources|Variables)\s*!", RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Supported = new(StringComparer.OrdinalIgnoreCase)
        { "Round", "Abs", "Min", "Max", "Upper", "Lower", "Trim", "Len", "IsNull", "Format" };

    /// <summary>True once a First/Last(...) call was turned into a plain field reference.</summary>
    public bool FirstUsed { get; private set; }

    private readonly Dictionary<string, string> _aggregateNames = new(StringComparer.Ordinal);
    private List<string> _literals = new();

    /// <summary>Returns the ClioReport expression, or null with <paramref name="problem"/> set.</summary>
    public string? Translate(string vb, string? scopeGroup, out string? problem)
    {
        problem = null;
        _literals = new List<string>();
        var s = Literal.Replace(vb.Trim().TrimStart('=').Trim(), m =>
        {
            var inner = m.Value[1..^1].Replace("\"\"", "\"").Replace("\\", "\\\\").Replace("'", "\\'");
            _literals.Add("'" + inner + "'");
            return "\u0001" + (_literals.Count - 1) + "\u0001";
        });

        var result = Scalar(s, scopeGroup, out problem);
        if (result is null) return null;

        if (Reference.IsMatch(result))
        {
            problem = "unsupported reference " + Reference.Match(result).Value.Trim();
            return null;
        }

        foreach (Match call in Call.Matches(result))
        {
            if (!Supported.Contains(call.Groups[1].Value) && !call.Groups[1].Value.StartsWith("agg", StringComparison.Ordinal))
            {
                problem = $"unsupported function {call.Groups[1].Value}";
                return null;
            }
        }

        return Regex.Replace(result, "\u0001(\\d+)\u0001", m => _literals[int.Parse(m.Groups[1].Value)]);
    }

    private string? Scalar(string s, string? scopeGroup, out string? problem)
    {
        problem = null;

        // Aggregates first: the call is replaced by the name of a generated aggregate.
        while (true)
        {
            var m = Aggregate.Match(s);
            if (!m.Success) break;

            // First/Last(Fields!X.Value, "DataSet") reads one value of a (usually one-row) dataset: ClioReport keeps such values as
            // plain fields of the root object, so the call becomes the field itself.
            if (m.Groups[1].Value.Equals("First", StringComparison.OrdinalIgnoreCase) || m.Groups[1].Value.Equals("Last", StringComparison.OrdinalIgnoreCase))
            {
                var fOpen = m.Index + m.Length - 1;
                var fClose = MatchingParen(s, fOpen);
                if (fClose < 0) { problem = "unbalanced parentheses"; return null; }
                var fArgs = SplitArguments(s[(fOpen + 1)..fClose]);
                if (fArgs.Count is < 1 or > 2) { problem = $"{m.Groups[1].Value} needs one or two arguments"; return null; }
                var fInner = Scalar(fArgs[0], scopeGroup, out problem);
                if (fInner is null) return null;
                FirstUsed = true;
                s = s[..m.Index] + "(" + fInner + ")" + s[(fClose + 1)..];
                continue;
            }

            var function = m.Groups[1].Value.ToLowerInvariant() switch
            {
                "sum" => (AggregateFunction?)AggregateFunction.Sum,
                "count" => AggregateFunction.Count,
                "avg" => AggregateFunction.Avg,
                "min" => AggregateFunction.Min,
                "max" => AggregateFunction.Max,
                _ => null
            };
            if (function is null)
            {
                problem = $"unsupported aggregate {m.Groups[1].Value}";
                return null;
            }

            var open = m.Index + m.Length - 1;
            var close = MatchingParen(s, open);
            if (close < 0)
            {
                problem = "unbalanced parentheses";
                return null;
            }

            var args = SplitArguments(s[(open + 1)..close]);
            if (args.Count is < 1 or > 2)
            {
                problem = $"{m.Groups[1].Value} needs one or two arguments";
                return null;
            }

            var inner = Scalar(args[0], scopeGroup, out problem);
            if (inner is null) return null;
            if (Aggregate.IsMatch(inner))
            {
                problem = "nested aggregates";
                return null;
            }

            string? group = scopeGroup;
            if (args.Count == 2)
            {
                var scope = args[1].Trim();
                var literal = Regex.Match(scope, "^\u0001(\\d+)\u0001$");
                var name = literal.Success ? _literals[int.Parse(literal.Groups[1].Value)].Trim('\'') : scope;
                group = groups.Contains(name) ? name : null;
            }

            var restoredInner = Regex.Replace(inner, "\u0001(\\d+)\u0001", x => _literals[int.Parse(x.Groups[1].Value)]);
            var key = $"{function}|{restoredInner}|{group}";
            if (!_aggregateNames.TryGetValue(key, out var aggName))
            {
                aggName = "agg" + (aggregates.Count + 1);
                aggregates.Add(new AggregateDefinition
                {
                    Name = aggName, Function = function.Value, Expression = restoredInner, Group = group
                });
                _aggregateNames[key] = aggName;
            }

            s = s[..m.Index] + aggName + s[(close + 1)..];
        }

        // IIf(condition, a, b) -> (condition ? a : b). Outer calls are rewritten first, so nesting resolves on the next pass.
        while (true)
        {
            var iif = Regex.Match(s, @"\bIIf\s*\(", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            if (!iif.Success) break;
            var iOpen = iif.Index + iif.Length - 1;
            var iClose = MatchingParen(s, iOpen);
            if (iClose < 0) { problem = "unbalanced parentheses"; return null; }
            var iArgs = SplitArguments(s[(iOpen + 1)..iClose]);
            if (iArgs.Count != 3) { problem = "IIf needs three arguments"; return null; }
            s = s[..iif.Index] + "((" + iArgs[0].Trim() + ") ? (" + iArgs[1].Trim() + ") : (" + iArgs[2].Trim() + "))" + s[(iClose + 1)..];
        }

        s = Regex.Replace(s, @"\bFields!(\w+)\.Value\b", "$1", RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"\bParameters!(\w+)\.Value\b", "$1", RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"\bGlobals!PageNumber\b", "PageNumber", RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"\bGlobals!(?:OverallTotalPages|TotalPages)\b", "PageCount", RegexOptions.CultureInvariant);
        s = Regex.Replace(s, @"\bUCase\b", "Upper", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bLCase\b", "Lower", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

        // "&" is VB string concatenation; "'' + (a) + (b)" forces text even when both sides are numbers.
        var parts = SplitTopLevel(s, '&');
        if (parts.Count > 1) s = "'' + " + string.Join(" + ", parts.Select(p => "(" + p.Trim() + ")"));

        s = s.Replace("<>", "!=");
        s = Regex.Replace(s, @"(?<![<>!=])=(?!=)", "==");
        s = Regex.Replace(s, @"\b(?:AndAlso|And)\b", "&&", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b(?:OrElse|Or)\b", "||", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bNot\b", "!", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bMod\b", "%", RegexOptions.IgnoreCase);
        return s;
    }

    private static int MatchingParen(string s, int open)
    {
        var depth = 0;
        for (var i = open; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')' && --depth == 0) return i;
        }
        return -1;
    }

    private static List<string> SplitArguments(string s) => SplitTopLevel(s, ',');

    private static List<string> SplitTopLevel(string s, char separator)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < s.Length; i++)
        {
            if (s[i] == '(') depth++;
            else if (s[i] == ')') depth--;
            else if (s[i] == separator && depth == 0)
            {
                parts.Add(s[start..i]);
                start = i + 1;
            }
        }
        parts.Add(s[start..]);
        return parts;
    }
}
