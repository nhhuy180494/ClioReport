namespace ClioReport.Xlsx;

/// <summary>Settings for <see cref="XlsxReportRenderer"/>.</summary>
public sealed class XlsxRenderOptions
{
    /// <summary>Name of the worksheet. Characters Excel does not allow are removed and the name is cut to 31 characters.</summary>
    public string SheetName { get; set; } = "Report";

    /// <summary>Elements whose top edges are within this many millimetres of the first element of a row share that row.</summary>
    public double RowTolerance { get; set; } = 1.0;

    /// <summary>Elements whose left edges are within this many millimetres of the first element of a column share that column.</summary>
    public double ColumnTolerance { get; set; } = 1.0;
}
