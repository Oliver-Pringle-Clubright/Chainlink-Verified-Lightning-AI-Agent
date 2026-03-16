using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightningAgentMarketPlace.Core.Configuration;
using LightningAgentMarketPlace.Core.Interfaces.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgentMarketPlace.AI;

/// <summary>
/// An IClaudeAiClient implementation that routes requests through OpenRouter's
/// OpenAI-compatible chat completions API. Falls back to the primary
/// <see cref="ClaudeApiClient"/> when OpenRouter is unavailable or misconfigured.
/// </summary>
public class MultiModelClient : IClaudeAiClient
{
    private readonly HttpClient _httpClient;
    private readonly OpenRouterSettings _settings;
    private readonly ClaudeApiClient _fallbackClient;
    private readonly ILogger<MultiModelClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
    };

    public MultiModelClient(
        HttpClient httpClient,
        IOptions<OpenRouterSettings> settings,
        ClaudeApiClient fallbackClient,
        ILogger<MultiModelClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _fallbackClient = fallbackClient;
        _logger = logger;
    }

    /// <summary>
    /// Selects the model to use for a given task type hint embedded in the system prompt.
    /// Falls back to <see cref="OpenRouterSettings.DefaultModel"/> when no mapping exists.
    /// </summary>
    public string SelectModel(string? taskTypeHint = null)
    {
        if (taskTypeHint is not null && _settings.TaskTypeModels.Count > 0)
        {
            foreach (var (taskType, model) in _settings.TaskTypeModels)
            {
                if (taskTypeHint.Contains(taskType, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "Selected model {Model} for task type hint '{TaskType}'",
                        model, taskType);
                    return model;
                }
            }
        }

        return _settings.DefaultModel;
    }

    public async Task<string> SendMessageAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_settings.ApiKey))
        {
            _logger.LogDebug("OpenRouter API key not configured, falling back to Claude");
            return await _fallbackClient.SendMessageAsync(systemPrompt, userMessage, ct);
        }

        try
        {
            return await SendViaOpenRouterAsync(systemPrompt, userMessage, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(
                ex,
                "OpenRouter request failed, falling back to Claude API");
            return await _fallbackClient.SendMessageAsync(systemPrompt, userMessage, ct);
        }
    }

    public async Task<T> SendStructuredRequestAsync<T>(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var enhancedPrompt = systemPrompt + "\n\nRespond with valid JSON only, no markdown code blocks.";

        var responseText = await SendMessageAsync(enhancedPrompt, userMessage, ct);

        var cleanedJson = ExtractJson(responseText);

        try
        {
            var result = JsonSerializer.Deserialize<T>(cleanedJson, JsonOptions);
            if (result is null)
            {
                throw new JsonException("Deserialization returned null.");
            }

            return result;
        }
        catch (JsonException ex)
        {
            _logger.LogError(
                ex,
                "Failed to deserialize OpenRouter response to {Type}. Response: {Response}",
                typeof(T).Name,
                responseText);
            throw new InvalidOperationException(
                $"Failed to parse response as {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    private async Task<string> SendViaOpenRouterAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct)
    {
        var model = SelectModel(systemPrompt);

        var requestBody = new
        {
            model,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userMessage }
            }
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var baseUrl = _settings.BaseUrl.TrimEnd('/');
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/chat/completions");
        request.Content = content;
        request.Headers.Add("Authorization", $"Bearer {_settings.ApiKey}");
        request.Headers.Add("HTTP-Referer", "https://lightning-agent.local");
        request.Headers.Add("X-Title", "LightningAgentMarketPlace");

        _logger.LogDebug("Sending request to OpenRouter with model {Model}", model);

        var response = await _httpClient.SendAsync(request, ct);
        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "OpenRouter API returned {StatusCode}: {Body}",
                response.StatusCode,
                responseBody);
            throw new HttpRequestException(
                $"OpenRouter API request failed with status {response.StatusCode}: {responseBody}");
        }

        var openRouterResponse = JsonSerializer.Deserialize<OpenRouterChatResponse>(responseBody, JsonOptions);

        if (openRouterResponse?.Choices is null || openRouterResponse.Choices.Count == 0)
        {
            _logger.LogWarning("OpenRouter API returned empty choices");
            throw new InvalidOperationException("OpenRouter API returned no choices.");
        }

        var messageContent = openRouterResponse.Choices[0].Message?.Content;
        if (string.IsNullOrEmpty(messageContent))
        {
            throw new InvalidOperationException("OpenRouter API returned empty message content.");
        }

        _logger.LogDebug(
            "OpenRouter response received. Model: {Model}, Finish reason: {FinishReason}",
            openRouterResponse.Model,
            openRouterResponse.Choices[0].FinishReason);

        return messageContent;
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        if (trimmed.StartsWith("```"))
        {
            var firstNewline = trimmed.IndexOf('\n');
            if (firstNewline >= 0)
            {
                trimmed = trimmed[(firstNewline + 1)..];
            }

            var lastFence = trimmed.LastIndexOf("```", StringComparison.Ordinal);
            if (lastFence >= 0)
            {
                trimmed = trimmed[..lastFence];
            }

            trimmed = trimmed.Trim();
        }

        return trimmed;
    }

    // ── OpenRouter OpenAI-compatible response types ──────────────────

    private sealed class OpenRouterChatResponse
    {
        public string? Id { get; set; }
        public string? Model { get; set; }
        public List<OpenRouterChoice> Choices { get; set; } = new();
    }

    private sealed class OpenRouterChoice
    {
        public OpenRouterMessage? Message { get; set; }
        public string? FinishReason { get; set; }
    }

    private sealed class OpenRouterMessage
    {
        public string? Role { get; set; }
        public string? Content { get; set; }
    }
}
