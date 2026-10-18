namespace ClioReport;

/// <summary>
/// Name lookup order: engine variables (aggregates, PageNumber, PageCount), then the current row, then the root context.
/// </summary>
internal sealed class EngineContext(IDataContext? row, IDataContext root, IReadOnlyDictionary<string, object?> variables)
    : IDataContext
{
    public bool TryGetValue(string name, out object? value)
    {
        if (variables.TryGetValue(name, out value)) return true;
        if (row is not null && row.TryGetValue(name, out value)) return true;
        return root.TryGetValue(name, out value);
    }

    public IEnumerable<IDataContext> GetRows(string source) => (row ?? root).GetRows(source);
}
