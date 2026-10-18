using System.Text.Json;
using System.Text.Json.Serialization;

namespace Atlas.BuildingBlocks;

public static class AtlasJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
