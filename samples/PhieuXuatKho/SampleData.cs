using ClioReport;

/// <summary>Minimal in-memory IDataContext used by the sample.</summary>
internal sealed class SampleContext : IDataContext
{
    private readonly Dictionary<string, object?> _values;
    private readonly Dictionary<string, List<SampleContext>> _rows = new();

    public SampleContext(Dictionary<string, object?> values) => _values = values;

    public bool TryGetValue(string name, out object? value) => _values.TryGetValue(name, out value);

    public IEnumerable<IDataContext> GetRows(string source) =>
        _rows.TryGetValue(source, out var rows) ? rows : Enumerable.Empty<IDataContext>();

    /// <summary>Three warehouses with 15 items each, already sorted by warehouse.</summary>
    public static SampleContext Create()
    {
        var root = new SampleContext(new()
        {
            ["company"] = "CÔNG TY TNHH MẪU",
            ["title"] = "PHIẾU XUẤT KHO",
            ["date"] = DateTime.Today.ToString("dd/MM/yyyy")
        });

        var items = new List<SampleContext>();
        foreach (var kho in new[] { "KHO A", "KHO B", "KHO C" })
        {
            for (var i = 1; i <= 15; i++)
            {
                items.Add(new SampleContext(new()
                {
                    ["kho"] = kho,
                    ["code"] = $"{kho[^1]}-{i:000}",
                    ["name"] = i % 4 == 0
                        ? $"Vật tư số {i} - loại đặc biệt dùng cho dây chuyền lắp ráp số 3, hàng dễ vỡ cần đóng gói riêng"
                        : $"Vật tư số {i}",
                    ["qty"] = i * 1250,
                    ["unit"] = "Cái"
                }));
            }
        }
        root._rows["items"] = items;
        return root;
    }
}
