namespace ClioReport;

/// <summary>Thrown when a report definition is invalid or cannot be rendered.</summary>
public sealed class ReportException : Exception
{
    public ReportException(string message, Exception? inner = null) : base(message, inner) { }
}
