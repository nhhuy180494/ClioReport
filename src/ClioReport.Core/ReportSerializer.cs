using System.Text.Json;
using System.Text.Json.Serialization;
using ClioReport.Model;

namespace ClioReport;

public static class ReportSerializer
{
    public const int CurrentVersion = 1;

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static ReportDefinition Deserialize(string json)
    {
        var def = JsonSerializer.Deserialize<ReportDefinition>(json, Options)
                  ?? throw new JsonException("Empty report definition.");

        if (def.Version < 1 || def.Version > CurrentVersion)
            throw new NotSupportedException(
                $"Report version {def.Version} is missing or unsupported (supported: 1..{CurrentVersion}).");

        return def;
    }

    public static string Serialize(ReportDefinition definition) =>
        JsonSerializer.Serialize(definition, Options);
}
