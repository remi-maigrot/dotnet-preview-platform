using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Devctl.Cli.Diagnostics;

/// <summary>
/// Optional explanation of a report by a language model, through any OpenAI-compatible chat completions endpoint
/// (a hosted service, a gateway, or a local server). Off unless DEVCTL_LLM_ENDPOINT is set; the report is
/// redacted before it is sent.
/// </summary>
internal sealed record AssistantSettings(Uri Endpoint, string Model, string? ApiKey)
{
    public const string EndpointVariable = "DEVCTL_LLM_ENDPOINT";
    public const string ModelVariable = "DEVCTL_LLM_MODEL";
    public const string ApiKeyVariable = "DEVCTL_LLM_API_KEY";

    public static AssistantSettings? FromEnvironment(Func<string, string?> variable)
    {
        ArgumentNullException.ThrowIfNull(variable);

        var endpoint = variable(EndpointVariable);
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            return null;
        }

        if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
        {
            throw new ArgumentException($"{EndpointVariable} must be an absolute http(s) URL, such as https://llm.example/v1.");
        }

        var model = variable(ModelVariable);
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new ArgumentException($"{ModelVariable} is required when {EndpointVariable} is set.");
        }

        return new AssistantSettings(uri, model, variable(ApiKeyVariable));
    }
}

internal sealed class Assistant(HttpClient http, AssistantSettings settings)
{
    private const string Instructions =
        "You help a developer understand why the preview environment of a .NET API on Kubernetes is failing. " +
        "Use only the report you are given. Answer in at most 12 lines: first the most likely cause, then the " +
        "commands or changes that fix it. When the report is not enough to tell, say which information is missing. " +
        "Values shown as <redacted> were removed on purpose.";

    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    /// <summary>The exact request body: what --show-payload prints and what <see cref="ExplainAsync"/> sends.</summary>
    public static string Payload(AssistantSettings settings, string report)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var body = new JsonObject
        {
            ["model"] = settings.Model,
            ["temperature"] = 0.2,
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = Instructions },
                new JsonObject { ["role"] = "user", ["content"] = Redactor.Redact(report) }),
        };
        return body.ToJsonString(Indented);
    }

    public async Task<string> ExplainAsync(string payload, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(settings.Endpoint.AbsoluteUri.TrimEnd('/') + "/chat/completions"))
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json"),
        };
        if (!string.IsNullOrEmpty(settings.ApiKey))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.ApiKey);
        }

        using var response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"The assistant endpoint answered {(int)response.StatusCode} {response.ReasonPhrase}.");
        }

        var answer = await response.Content.ReadFromJsonAsync<JsonObject>(cancellationToken);
        return answer?["choices"]?[0]?["message"]?["content"]?.GetValue<string>()
            ?? throw new HttpRequestException("The assistant endpoint answered without choices[0].message.content.");
    }
}
