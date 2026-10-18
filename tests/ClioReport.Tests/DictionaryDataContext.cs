namespace ClioReport.Tests;

/// <summary>Simple in-memory IDataContext for tests.</summary>
internal sealed class DictionaryDataContext : IDataContext
{
    private readonly Dictionary<string, object?> _values;

    public DictionaryDataContext(Dictionary<string, object?>? values = null) =>
        _values = values ?? new Dictionary<string, object?>();

    public Dictionary<string, List<DictionaryDataContext>> Rows { get; } = new();

    public bool TryGetValue(string name, out object? value) => _values.TryGetValue(name, out value);

    public IEnumerable<IDataContext> GetRows(string source) =>
        Rows.TryGetValue(source, out var rows) ? rows : Enumerable.Empty<IDataContext>();
}
