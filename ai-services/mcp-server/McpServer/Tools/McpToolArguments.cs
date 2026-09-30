using System.Diagnostics.CodeAnalysis;
using System.Text.Json;

namespace McpServer.Tools;

internal static class McpToolArguments
{
    public static bool IsSupportedFeature([NotNullWhen(true)] string? feature) => feature is not null
        && feature.StartsWith("student-", StringComparison.OrdinalIgnoreCase)
        && int.TryParse(feature[8..], out var number)
        && number is >= 1 and <= 5;

    public static string? GetString(
        IReadOnlyDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.String } element => element.GetString(),
            string text => text,
            _ => null
        };
    }

    public static int? GetInt32(
        IReadOnlyDictionary<string, object?> arguments,
        string name)
    {
        if (!arguments.TryGetValue(name, out var value))
        {
            return null;
        }

        return value switch
        {
            JsonElement { ValueKind: JsonValueKind.Number } element when element.TryGetInt32(out var number) => number,
            int number => number,
            _ => null
        };
    }
}

public sealed class McpToolArgumentException(string message)
    : Exception(message);
