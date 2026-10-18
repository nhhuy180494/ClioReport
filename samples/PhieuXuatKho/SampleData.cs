using ClioReport;

internal sealed record Item(string Kho, string Code, string Name, int Qty, string Unit);

internal static class SampleData
{
    /// <summary>Three warehouses with 15 items each, already sorted by warehouse.</summary>
    public static IDataContext Create()
    {
        var items = new List<Item>();
        foreach (var kho in new[] { "KHO A", "KHO B", "KHO C" })
        {
            for (var i = 1; i <= 15; i++)
            {
                var name = i % 4 == 0
                    ? $"Vật tư số {i} - loại đặc biệt dùng cho dây chuyền lắp ráp số 3, hàng dễ vỡ cần đóng gói riêng"
                    : $"Vật tư số {i}";
                items.Add(new Item(kho, $"{kho[^1]}-{i:000}", name, i * 1250, "Cái"));
            }
        }

        return new ObjectDataContext(
            new { Company = "CÔNG TY TNHH MẪU", Title = "PHIẾU XUẤT KHO", Date = DateTime.Today.ToString("dd/MM/yyyy") },
            new Dictionary<string, object> { ["items"] = items });
    }
}
