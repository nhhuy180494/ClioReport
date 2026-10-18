namespace ClioReport.Expressions;

/// <summary>
/// Sandboxed expression: literals, fields from <see cref="IDataContext"/>, operators and a fixed
/// whitelist of functions. No reflection, no member calls, no access outside the data context.
/// </summary>
public sealed class Expression
{
    public const int MaxLength = 2000;

    private readonly Node _root;

    private Expression(string source, Node root)
    {
        Source = source;
        _root = root;
    }

    public string Source { get; }

    public static Expression Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            throw new ExpressionException("Expression is empty.");
        if (text.Length > MaxLength)
            throw new ExpressionException($"Expression is longer than {MaxLength} characters.");

        return new Expression(text, new Parser(text).ParseAll());
    }

    /// <summary>Returns decimal, string, bool, DateTime or null (other field types pass through opaque).</summary>
    public object? Evaluate(IDataContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        try
        {
            return _root.Eval(context);
        }
        catch (OverflowException)
        {
            throw new ExpressionException("Numeric overflow.");
        }
        catch (FormatException ex)
        {
            throw new ExpressionException($"Invalid format: {ex.Message}");
        }
    }
}
