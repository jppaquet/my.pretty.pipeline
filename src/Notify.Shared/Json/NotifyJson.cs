using System.Text.Json;
using System.Text.Json.Serialization;

namespace Notify.Shared.Json;

public static class NotifyJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter<Priority>(JsonNamingPolicy.CamelCase) },
    };
}
