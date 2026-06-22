using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace UnityIA.Cli;

internal sealed record HttpIntentProviderOptions(
    Uri Endpoint,
    string? BearerToken = null,
    bool AllowInsecureLoopback = false,
    TimeSpan? Timeout = null);

internal sealed class HttpIntentCommandProvider : IIntentCommandProvider
{
    private static readonly string[] SupportedIntents =
    [
        "read_context",
        "validate_active_scene",
        "create_gameobject"
    ];

    private readonly HttpClient httpClient;
    private readonly HttpIntentProviderOptions options;
    private readonly StructuredIntentCommandProvider structuredProvider;

    public HttpIntentCommandProvider(
        HttpClient httpClient,
        HttpIntentProviderOptions options,
        StructuredIntentCommandProvider structuredProvider)
    {
        this.httpClient = httpClient;
        this.options = options;
        this.structuredProvider = structuredProvider;
    }

    public async Task<IntentProviderResponse> ProposeCommandsAsync(
        IntentPlanningRequest request,
        CancellationToken cancellationToken)
    {
        IntentProviderResponse? configurationError = ValidateOptions();
        if (configurationError is not null)
        {
            return configurationError;
        }

        using HttpRequestMessage httpRequest = new(HttpMethod.Post, options.Endpoint);
        if (!string.IsNullOrWhiteSpace(options.BearerToken))
        {
            httpRequest.Headers.Authorization =
                new AuthenticationHeaderValue("Bearer", options.BearerToken);
        }

        if (options.Timeout.HasValue)
        {
            httpRequest.Options.Set(
                new HttpRequestOptionsKey<TimeSpan>("UnityIA-Intent-Timeout"),
                options.Timeout.Value);
        }

        httpRequest.Content = new StringContent(
            JsonSerializer.Serialize(new
            {
                requestId = request.RequestId,
                prompt = request.Prompt,
                promptSha256 = IntentTraceHash.Sha256(request.Prompt),
                supportedIntents = SupportedIntents
            }),
            Encoding.UTF8,
            "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpClient.SendAsync(httpRequest, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TaskCanceledException)
        {
            return Fail(
                "PROVIDER_TRANSPORT_ERROR",
                "Intent provider request failed: " + exception.Message);
        }

        using (response)
        {
            string body = await response.Content.ReadAsStringAsync(cancellationToken)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return Fail(
                    "PROVIDER_TRANSPORT_ERROR",
                    "Intent provider returned HTTP " + (int)response.StatusCode + ".");
            }

            if (!TryReadProviderResponse(body, out string structuredIntent, out string[] warnings, out string error))
            {
                return Fail("INVALID_PROVIDER_RESPONSE", error);
            }

            IntentProviderResponse structuredResponse =
                await structuredProvider.ProposeCommandsAsync(
                        request with { Prompt = structuredIntent },
                        cancellationToken)
                    .ConfigureAwait(false);
            return structuredResponse.Success
                ? new IntentProviderResponse(
                    structuredResponse.CommandJson,
                    structuredResponse.Warnings.Concat(warnings).ToArray())
                : structuredResponse;
        }
    }

    private IntentProviderResponse? ValidateOptions()
    {
        if (!options.Endpoint.IsAbsoluteUri ||
            options.Endpoint.Scheme is not ("https" or "http"))
        {
            return Fail("INVALID_PROVIDER_CONFIG", "Provider endpoint must be absolute HTTP(S).");
        }

        if (options.Endpoint.Scheme == "http" &&
            !(options.AllowInsecureLoopback && IsLoopback(options.Endpoint)))
        {
            return Fail(
                "INVALID_PROVIDER_CONFIG",
                "Provider endpoint must use HTTPS unless insecure loopback is explicitly allowed.");
        }

        return null;
    }

    private static bool TryReadProviderResponse(
        string json,
        out string structuredIntent,
        out string[] warnings,
        out string error)
    {
        structuredIntent = string.Empty;
        warnings = [];
        error = string.Empty;
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = "Provider response root must be an object.";
                return false;
            }

            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (property.Name is not ("intent" or "warnings"))
                {
                    error = "Unsupported provider response property: " + property.Name;
                    return false;
                }
            }

            if (!root.TryGetProperty("intent", out JsonElement intent) ||
                intent.ValueKind != JsonValueKind.Object)
            {
                error = "Provider response must contain an intent object.";
                return false;
            }

            if (root.TryGetProperty("warnings", out JsonElement warningsElement))
            {
                if (warningsElement.ValueKind != JsonValueKind.Array)
                {
                    error = "Provider warnings must be an array.";
                    return false;
                }

                List<string> parsedWarnings = [];
                foreach (JsonElement warning in warningsElement.EnumerateArray())
                {
                    if (warning.ValueKind != JsonValueKind.String)
                    {
                        error = "Provider warnings must be strings.";
                        return false;
                    }

                    string? value = warning.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        parsedWarnings.Add(value);
                    }
                }

                warnings = parsedWarnings.ToArray();
            }

            structuredIntent = intent.GetRawText();
            return true;
        }
        catch (JsonException exception)
        {
            error = "Invalid provider response JSON: " + exception.Message;
            return false;
        }
    }

    private static bool IsLoopback(Uri endpoint)
    {
        return endpoint.IsLoopback ||
            endpoint.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            endpoint.Host.Equals("127.0.0.1", StringComparison.Ordinal) ||
            endpoint.Host.Equals("::1", StringComparison.Ordinal);
    }

    private static IntentProviderResponse Fail(string code, string message)
    {
        return new IntentProviderResponse([], [], false, code, message);
    }
}
