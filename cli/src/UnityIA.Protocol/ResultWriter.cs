using System.Text.Json;

namespace UnityIA.Protocol;

public static class ResultWriter
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string Success(string message, object? data = null)
    {
        return Serialize(true, message, "OK", data ?? new { warnings = Array.Empty<string>() });
    }

    public static string Error(string code, string message, object? data = null)
    {
        return Serialize(false, message, code, data ?? new { warnings = Array.Empty<string>() });
    }

    private static string Serialize(bool success, string message, string code, object data)
    {
        return JsonSerializer.Serialize(
            new
            {
                success,
                message,
                code,
                data
            },
            Options);
    }
}

