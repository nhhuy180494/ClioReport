namespace ClioReport.Model;

public sealed class GroupDefinition
{
    public string Name { get; set; } = "";

    /// <summary>Expression whose value change starts a new group.</summary>
    public string Expression { get; set; } = "";

    /// <summary>Start every group instance on a new page (the very first one never leaves an empty page).</summary>
    public bool PageBreakBefore { get; set; }
}

public enum AggregateFunction
{
    Sum,
    Count,
    Avg,
    Min,
    Max
}

public sealed class AggregateDefinition
{
    public string Name { get; set; } = "";

    public AggregateFunction Function { get; set; }

    public string Expression { get; set; } = "";

    /// <summary>Group name the aggregate resets on; null means whole report.</summary>
    public string? Group { get; set; }
}
