using System.Collections;
using System.Collections.Concurrent;
using System.Data;
using System.Reflection;

namespace ClioReport;

/// <summary>
/// <see cref="IDataContext"/> over plain objects, so you rarely need to implement the interface yourself.
/// </summary>
/// <remarks>
/// Field names are looked up on the root object and on each row. A segment is resolved as a key of an
/// <c>IDictionary&lt;string, object?&gt;</c>, a column of a <see cref="DataRow"/>/<see cref="DataRowView"/>
/// (DBNull becomes null) or a public instance property, ignoring case. Dotted names walk nested objects and a null
/// in the middle gives null. Row collections come from <c>sources</c> by name, or from a collection property of the
/// root with that name; a source can be any <see cref="IEnumerable"/> (List&lt;T&gt;, DataView, ...) or a
/// <see cref="DataTable"/>. Only property reads happen: no methods are ever called.
/// </remarks>
public sealed class ObjectDataContext : IDataContext
{
    private static readonly ConcurrentDictionary<(Type, string), PropertyInfo?> Properties = new();

    private readonly object? _root;
    private readonly Dictionary<string, object>? _sources;

    /// <param name="root">Object whose properties are the root fields (header values); may be null.</param>
    /// <param name="sources">Named row collections, e.g. <c>["items"] = list</c>; may be null.</param>
    public ObjectDataContext(object? root = null, IDictionary<string, object>? sources = null)
    {
        _root = root;
        if (sources is not null)
            _sources = new Dictionary<string, object>(sources, StringComparer.OrdinalIgnoreCase);
    }

    public bool TryGetValue(string name, out object? value)
    {
        value = null;
        if (_root is null) return false;

        var current = _root;
        foreach (var segment in name.Split('.'))
        {
            if (current is null) return true; // null propagates
            if (!TryGetMember(current, segment, out var next)) return false;
            current = next;
        }

        value = current;
        return true;
    }

    public IEnumerable<IDataContext> GetRows(string source)
    {
        object? rows = null;
        if (_sources is null || !_sources.TryGetValue(source, out rows))
            TryGetValue(source, out rows);

        if (rows is null) return Enumerable.Empty<IDataContext>();

        IEnumerable? sequence = rows switch
        {
            DataTable table => table.Rows,
            string => null,
            IEnumerable e => e,
            _ => null
        };
        if (sequence is null)
            throw new ReportException($"Data source '{source}' is not a collection.");

        return Wrap(sequence);
    }

    private static IEnumerable<IDataContext> Wrap(IEnumerable sequence)
    {
        foreach (var row in sequence)
            yield return row as IDataContext ?? new ObjectDataContext(row);
    }

    private static bool TryGetMember(object target, string name, out object? value)
    {
        value = null;

        if (target is DataRowView view) target = view.Row;
        if (target is DataRow row)
        {
            if (!row.Table.Columns.Contains(name)) return false;
            var cell = row[name];
            value = cell is DBNull ? null : cell;
            return true;
        }

        if (target is IDictionary<string, object?> dictionary)
        {
            if (dictionary.TryGetValue(name, out value)) return true;
            foreach (var pair in dictionary)
            {
                if (!string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase)) continue;
                value = pair.Value;
                return true;
            }
            return false;
        }

        var property = Properties.GetOrAdd((target.GetType(), name), static key => FindProperty(key.Item1, key.Item2));
        if (property is null) return false;

        value = property.GetValue(target);
        return true;
    }

    private static PropertyInfo? FindProperty(Type type, string name)
    {
        try
        {
            var property = type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance)
                           ?? type.GetProperty(name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            return property is not null && property.GetIndexParameters().Length == 0 ? property : null;
        }
        catch (AmbiguousMatchException)
        {
            return null;
        }
    }
}
