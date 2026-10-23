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

    /// <summary>
    /// Fields the report is designed against (names and types). Used only by the designer to list fields; the engine
    /// ignores it.
    /// </summary>
    public ReportSchema? Schema { get; set; }
}

/// <summary>Field list of the data a report expects, for the designer.</summary>
public sealed class ReportSchema
{
    /// <summary>Fields of the root object (header and footer values).</summary>
    public List<SchemaField> Root { get; set; } = new();

    /// <summary>Row collections with the fields of one row.</summary>
    public List<SchemaCollection> Collections { get; set; } = new();
}

public sealed class SchemaField
{
    public string Name { get; set; } = "";

    /// <summary>"text", "number" or "date".</summary>
    public string Type { get; set; } = "text";
}

public sealed class SchemaCollection
{
    public string Name { get; set; } = "";

    public List<SchemaField> Fields { get; set; } = new();
}

/// <summary>Page size and margin, in millimetres.</summary>
public sealed class PageSettings
{
    public double Width { get; set; } = 210;
    public double Height { get; set; } = 297;
    public double Margin { get; set; } = 10;

    /// <summary>
    /// Number of detail columns (labels). Above 1 the single detail band is repeated left to right, then top to
    /// bottom, in cells of equal width; other bands stay full width.
    /// </summary>
    public int Columns { get; set; } = 1;

    /// <summary>Space between detail columns in millimetres.</summary>
    public double ColumnGap { get; set; }
}
