using ClioReport;

internal sealed record Line(
    string Code, string Name, string Barcode, string Unit, byte[] Image, decimal Qty, decimal Price, decimal Amount, string Remark);

internal static class SampleData
{
    // Small grey placeholder picture standing in for a product photo.
    private const string PlaceholderPng = "iVBORw0KGgoAAAANSUhEUgAAAEAAAAAsCAIAAABaPSmoAAAA6UlEQVR4nO3Tyw3EIAxFUZdNJRSR1mhiFggpmTjGnww2I781oHsWQNt84B1gXQK8lwDvxQIcY/wrUQAHNs7FBLwxtJ5pSMAb2x7Qdv8D7Q8AfaL0vlgAxRLgvQR4LwHeS4Bqdcz+lAOgXmd8bTWgYrM8uBSA1hsNVkAZm54k6i0GE6BcR5yc1qsNekDBZqnXGZQAtB41iOoVBjGASL8zFPVSgwzArO9T14sMAsDKer6BC1hfzzSwAF71HMMEIEr/Rf3UQAGC1NOGR0CoesKAAwLWPxkQQNh61ACW9PX1dwNsV/9lgB3rz4YP3twJvkiW9I4AAAAASUVORK5CYII=";

    public static IDataContext Create()
    {
        var image = Convert.FromBase64String(PlaceholderPng);
        var items = new List<Line>
        {
            new("M-AL-A071",
                "(M)원자재(구매)/알루미늄/해당없음/해당없음/Aluminum/골드반무광/홈온(길이21.2mm)-(BELLAZ_BBW 8종)/",
                "261015-M-01", "EA", image, 39410, 120, 0, ""),
            new("M-GL-B002", "Kính cường lực 5mm, cắt theo bản vẽ số 12 (hàng dễ vỡ)", "261015-M-02", "PCS", image, 1250, 85000, 106250000, ""),
        };

        return new ObjectDataContext(
            new
            {
                DocNo = "1",
                DocNo2 = "1",
                Date = "2026-10-15",
                Dept = "[]",
                Fullname = "[ku19] 민수 김",
                Vendor = "[S04093] 주식회사 남동금속",
                Address = "",
                TaxCode = "",
                Tel = "",
                No = "0",
                Currency = "",
                Subtotal = 106250000m,
                Tax = 0m,
                Total = 106250000m
            },
            new Dictionary<string, object> { ["items"] = items });
    }
}
