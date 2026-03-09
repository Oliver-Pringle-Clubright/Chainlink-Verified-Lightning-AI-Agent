using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightningAgent.Acp;

/// <summary>
/// Static JSON serialization helpers for ACP protocol messages.
/// </summary>
public static class AcpMessageSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    /// <summary>
    /// Serializes an object to a JSON string using ACP conventions (camelCase, null-ignoring).
    /// </summary>
    public static string Serialize<T>(T obj)
    {
        return JsonSerializer.Serialize(obj, Options);
    }

    /// <summary>
    /// Deserializes a JSON string to the specified type using ACP conventions.
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        return JsonSerializer.Deserialize<T>(json, Options);
    }
}
