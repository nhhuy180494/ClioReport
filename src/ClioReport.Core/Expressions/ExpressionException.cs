namespace ClioReport.Expressions;

/// <summary>Thrown for any syntax or evaluation error in a report expression.</summary>
public sealed class ExpressionException : Exception
{
    public ExpressionException(string message) : base(message) { }
}
