namespace ClioReport;

/// <summary>Measures text so the engine can wrap it. ClioReport.Pdf provides one that matches its font.</summary>
public interface ITextMeasurer
{
    /// <summary>Width of a single line of text, in millimetres.</summary>
    double MeasureWidth(string text);

    /// <summary>Height of one line of text, in millimetres.</summary>
    double LineHeight { get; }
}
