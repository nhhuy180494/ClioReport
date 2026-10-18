using ClioReport.Model;

namespace ClioReport;

public static class ReportEngine
{
    /// <summary>
    /// Lays out the report. Detail rows come from <c>data.GetRows(definition.DataSource)</c> and must already be
    /// sorted by the group expressions (outer group first). A measurer is required only when elements use
    /// <c>wrap</c>.
    /// </summary>
    public static RenderedReport Render(ReportDefinition definition, IDataContext data, ITextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(data);
        return new ReportRunner(definition, data, measurer).Execute();
    }
}
