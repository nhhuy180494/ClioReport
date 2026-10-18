using System.Data;
using ClioReport.Expressions;
using Xunit;

namespace ClioReport.Tests;

public class ObjectDataContextTests
{
    private sealed record Item(string Kho, string Code, int Qty);

    private const string Json = """
    {
      "version": 1,
      "dataSource": "items",
      "bands": [
        { "type": "reportHeader", "height": 5, "elements": [ { "width": 50, "height": 5, "expression": "title" } ] },
        { "type": "detail", "height": 5, "elements": [
          { "width": 30, "height": 5, "expression": "code" },
          { "x": 30, "width": 20, "height": 5, "expression": "qty" } ] }
      ]
    }
    """;

    private static List<string> Texts(RenderedReport report) =>
        report.Pages.SelectMany(p => p.Elements).Select(e => e.Text).ToList();

    private static readonly List<Item> Items = new() { new("A", "a1", 2), new("A", "a2", 3) };

    [Fact]
    public void Poco_RowsFromSources_PropertiesMatchIgnoringCase()
    {
        var data = new ObjectDataContext(
            new { Title = "PHIEU" },
            new Dictionary<string, object> { ["items"] = Items });

        var texts = Texts(ReportEngine.Render(ReportSerializer.Deserialize(Json), data));

        Assert.Equal(new[] { "PHIEU", "a1", "2", "a2", "3" }, texts);
    }

    [Fact]
    public void Poco_RowsFromRootCollectionProperty()
    {
        var data = new ObjectDataContext(new { Title = "T", Items });

        var texts = Texts(ReportEngine.Render(ReportSerializer.Deserialize(Json), data));

        Assert.Equal(new[] { "T", "a1", "2", "a2", "3" }, texts);
    }

    [Fact]
    public void Dictionary_Rows()
    {
        var rows = new List<Dictionary<string, object?>>
        {
            new() { ["code"] = "x", ["qty"] = 7 },
            new() { ["CODE"] = "y", ["qty"] = 8 }
        };
        var data = new ObjectDataContext(
            new Dictionary<string, object?> { ["title"] = "D" },
            new Dictionary<string, object> { ["items"] = rows });

        var texts = Texts(ReportEngine.Render(ReportSerializer.Deserialize(Json), data));

        Assert.Equal(new[] { "D", "x", "7", "y", "8" }, texts);
    }

    [Fact]
    public void DataTable_Rows_DbNullBecomesNull()
    {
        var table = new DataTable();
        table.Columns.Add("code", typeof(string));
        table.Columns.Add("note", typeof(string));
        table.Rows.Add("a1", "ok");
        table.Rows.Add("a2", DBNull.Value);

        var def = ReportSerializer.Deserialize("""
        {
          "version": 1,
          "dataSource": "items",
          "bands": [ { "type": "detail", "height": 5, "elements": [
            { "width": 30, "height": 5, "expression": "code" },
            { "x": 30, "width": 30, "height": 5, "expression": "IsNull(note, 'n/a')" } ] } ]
        }
        """);
        var data = new ObjectDataContext(null, new Dictionary<string, object> { ["items"] = table });

        var texts = Texts(ReportEngine.Render(def, data));

        Assert.Equal(new[] { "a1", "ok", "a2", "n/a" }, texts);
    }

    [Fact]
    public void NestedPath_WalksObjects_AndNullPropagates()
    {
        var ctx = new ObjectDataContext(new { Customer = new { Name = "A" } });
        var nullCtx = new ObjectDataContext(new { Customer = (object?)null });

        Assert.Equal("A", Expression.Parse("customer.name").Evaluate(ctx));
        Assert.Null(Expression.Parse("customer.name").Evaluate(nullCtx));
    }

    [Fact]
    public void UnknownField_Throws() =>
        Assert.Throws<ExpressionException>(
            () => Expression.Parse("nope").Evaluate(new ObjectDataContext(new { A = 1 })));

    [Fact]
    public void NonCollectionSource_Throws() =>
        Assert.Throws<ReportException>(
            () => ReportEngine.Render(ReportSerializer.Deserialize(Json), new ObjectDataContext(new { Items = 5 })));
}
