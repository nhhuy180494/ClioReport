using Xunit;

namespace ClioReport.Tests;

public class ReportNestedGroupsTests
{
    // Two group levels: kho (outer) and loai (inner). The page is tall enough for everything on one page.
    private const string Json = """
    {
      "version": 1,
      "dataSource": "items",
      "page": { "width": 210, "height": 297, "margin": 5 },
      "groups": [ { "name": "kho", "expression": "kho" }, { "name": "loai", "expression": "loai" } ],
      "bands": [
        { "type": "groupHeader", "group": "kho", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Kho ' + kho" } ] },
        { "type": "groupHeader", "group": "loai", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Loai ' + loai" } ] },
        { "type": "detail", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 30, "height": 5, "expression": "qty" } ] },
        { "type": "groupFooter", "group": "loai", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Tong ' + kho + '/' + loai + ': ' + tLoai" } ] },
        { "type": "groupFooter", "group": "kho", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Tong kho ' + kho + ': ' + tKho" } ] },
        { "type": "reportFooter", "height": 5, "elements": [
          { "x": 0, "y": 0, "width": 50, "height": 5, "expression": "'Tong cong: ' + tong" } ] }
      ],
      "aggregates": [
        { "name": "tLoai", "function": "sum", "expression": "qty", "group": "loai" },
        { "name": "tKho", "function": "sum", "expression": "qty", "group": "kho" },
        { "name": "tong", "function": "sum", "expression": "qty" }
      ]
    }
    """;

    private sealed record Row(string Kho, string Loai, int Qty);

    private static RenderedReport Render(params Row[] rows)
    {
        var data = new ObjectDataContext(new { }, new Dictionary<string, object> { ["items"] = rows.ToList() });
        return ReportEngine.Render(ReportSerializer.Deserialize(Json), data);
    }

    [Fact]
    public void InnerGroupsCloseFirst_AndAggregatesResetPerLevel()
    {
        // B reuses the inner value "y": the inner group must still restart because the outer one changed.
        var r = Render(new Row("A", "x", 1), new Row("A", "x", 2), new Row("A", "y", 3), new Row("B", "y", 4));

        var texts = r.Pages.SelectMany(p => p.Elements).Select(e => e.Text).ToList();

        Assert.Equal(new[]
        {
            "Kho A", "Loai x", "1", "2", "Tong A/x: 3",
            "Loai y", "3", "Tong A/y: 3", "Tong kho A: 6",
            "Kho B", "Loai y", "4", "Tong B/y: 4", "Tong kho B: 4",
            "Tong cong: 10"
        }, texts);
    }
}
