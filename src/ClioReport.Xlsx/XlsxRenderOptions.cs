namespace ClioReport.Xlsx;

/// <summary>Settings for <see cref="XlsxReportRenderer"/>.</summary>
public sealed class XlsxRenderOptions
{
    /// <summary>Name of the worksheet. Characters Excel does not allow are removed and the name is cut to 31 characters.</summary>
    public string SheetName { get; set; } = "Report";

    /// <summary>Top and bottom edges closer than this many millimetres count as the same row boundary.</summary>
    public double RowTolerance { get; set; } = 1.0;

    /// <summary>Width in millimetres of every column of the sheet. Elements are placed on this grid and merged across the columns they cover; a smaller value follows the layout more exactly but gives more columns.</summary>
    public double ColumnWidth { get; set; } = 2.5;

    /// <summary>Show Excel's grid lines on the sheet. Off by default: the sheet then looks like the report.</summary>
    public bool ShowGridLines { get; set; }

    /// <summary>
    /// Print like the report (on by default): every page of the report starts a new printed page, the paper is the size of
    /// the report page and the margins put the content where it sits on that page. Off: the pages run down one sheet with a
    /// blank row between them, printed on A4 and fitted to its width.
    /// </summary>
    public bool PrintPages { get; set; } = true;
}
