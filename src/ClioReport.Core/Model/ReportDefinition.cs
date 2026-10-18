namespace ClioReport.Model;

public sealed class ReportDefinition
{
    /// <summary>Schema version of the JSON file. Required.</summary>
    public int Version { get; set; }

    public string Name { get; set; } = "";

    /// <summary>Name of the collection passed to <see cref="IDataContext.GetRows"/> for detail rows.</summary>
    public string DataSource { get; set; } = "";

    public PageSettings Page { get; set; } = new();

    public List<GroupDefinition> Groups { get; set; } = new();

    public List<BandDefinition> Bands { get; set; } = new();

    public List<AggregateDefinition> Aggregates { get; set; } = new();
}

/// <summary>Page size and margin, in millimetres.</summary>
public sealed class PageSettings
{
    public double Width { get; set; } = 210;
    public double Height { get; set; } = 297;
    public double Margin { get; set; } = 10;
}
