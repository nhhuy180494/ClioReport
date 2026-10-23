using ClioReport.Model;
using Xunit;

namespace ClioReport.Tests;

public class ReportSerializerTests
{
    private const string Json = """
    {
      "version": 1,
      "name": "PhieuXuatKho",
      "dataSource": "items",
      "groups": [ { "name": "kho", "expression": "item.kho" } ],
      "bands": [
        {
          "type": "detail",
          "height": 6,
          "elements": [ { "type": "text", "x": 0, "y": 0, "width": 40, "height": 6, "expression": "item.code" } ]
        }
      ],
      "aggregates": [ { "name": "tongSL", "function": "sum", "expression": "item.qty", "group": "kho" } ]
    }
    """;

    [Fact]
    public void Deserialize_ValidJson_MapsFields()
    {
        var def = ReportSerializer.Deserialize(Json);

        Assert.Equal("PhieuXuatKho", def.Name);
        Assert.Equal(210, def.Page.Width);
        Assert.Equal("kho", Assert.Single(def.Groups).Name);
        var band = Assert.Single(def.Bands);
        Assert.Equal(BandType.Detail, band.Type);
        Assert.Equal("item.code", Assert.Single(band.Elements).Expression);
        Assert.Equal(AggregateFunction.Sum, Assert.Single(def.Aggregates).Function);
    }

    [Fact]
    public void Serialize_RoundTrips()
    {
        var def = ReportSerializer.Deserialize(Json);

        var again = ReportSerializer.Deserialize(ReportSerializer.Serialize(def));

        Assert.Equal(def.Name, again.Name);
        Assert.Equal(def.Bands.Count, again.Bands.Count);
    }

    [Fact]
    public void Schema_IsOptional_AndRoundTrips()
    {
        Assert.Null(ReportSerializer.Deserialize(Json).Schema);

        var withSchema = Json.Replace("\"aggregates\"", "\"schema\": { \"root\": [ { \"name\": \"DocNo\", \"type\": \"text\" } ], \"collections\": [ { \"name\": \"items\", \"fields\": [ { \"name\": \"Qty\", \"type\": \"number\" } ] } ] }, \"aggregates\"");
        var again = ReportSerializer.Deserialize(ReportSerializer.Serialize(ReportSerializer.Deserialize(withSchema)));

        Assert.Equal("DocNo", Assert.Single(again.Schema!.Root).Name);
        var items = Assert.Single(again.Schema.Collections);
        Assert.Equal("items", items.Name);
        Assert.Equal("number", Assert.Single(items.Fields).Type);
    }

    [Theory]
    [InlineData("""{ "name": "x" }""")]
    [InlineData("""{ "version": 99 }""")]
    public void Deserialize_MissingOrUnsupportedVersion_Throws(string json)
    {
        Assert.Throws<NotSupportedException>(() => ReportSerializer.Deserialize(json));
    }
}
