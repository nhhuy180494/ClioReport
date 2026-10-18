namespace ClioReport;

/// <summary>
/// Data source abstraction used by the engine. Implement it over DataTable, POCO lists, JSON, etc.
/// </summary>
public interface IDataContext
{
    /// <summary>Resolves a field by name (dotted paths allowed). Returns false when not found.</summary>
    bool TryGetValue(string name, out object? value);

    /// <summary>Rows of a named collection, each exposed as its own context.</summary>
    IEnumerable<IDataContext> GetRows(string source);
}
