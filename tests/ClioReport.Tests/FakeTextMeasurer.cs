namespace ClioReport.Tests;

/// <summary>2 mm per character, 5 mm per line: makes wrapping results easy to predict.</summary>
internal sealed class FakeTextMeasurer : ITextMeasurer
{
    public double MeasureWidth(string text) => text.Length * 2;

    public double LineHeight => 5;
}
