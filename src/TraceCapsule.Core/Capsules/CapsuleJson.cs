using System.Text.Json;
using System.Text.Json.Serialization;

namespace TraceCapsule.Core.Capsules;

internal static class CapsuleJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };
}
