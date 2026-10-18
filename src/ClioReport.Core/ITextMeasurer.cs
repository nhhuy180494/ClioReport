namespace ClioReport;

/// <summary>Measures text so the engine can wrap it. ClioReport.Pdf provides one that matches its font.</summary>
public interface ITextMeasurer
{
    /// <summary>Width of a single line of text, in millimetres.</summary>
    double MeasureWidth(string text);

    /// <summary>Height of one line of text, in millimetres.</summary>
    double LineHeight { get; }

    /// <summary>Width of a line in the given style. Implementations that support styles should override it.</summary>
    double MeasureWidth(string text, TextStyle style) => MeasureWidth(text);

    /// <summary>Height of one line in the given style. Implementations that support styles should override it.</summary>
    double GetLineHeight(TextStyle style) => LineHeight;
}
