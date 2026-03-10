using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LightningAgent.Core.Configuration;
using LightningAgent.Core.Interfaces.Services;
using LightningAgent.Core.Models.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace LightningAgent.AI;

public class ClaudeApiClient : IClaudeAiClient
{
    private readonly HttpClient _httpClient;
    private readonly ClaudeAiSettings _settings;
    private readonly ILogger<ClaudeApiClient> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: true) }
    };

    public ClaudeApiClient(
        HttpClient httpClient,
        IOptions<ClaudeAiSettings> settings,
        ILogger<ClaudeApiClient> logger)
    {
        _httpClient = httpClient;
        _settings = settings.Value;
        _logger = logger;
    }

    public async Task<string> SendMessageAsync(
        string systemPrompt,
        string userMessage,
        CancellationToken ct = default)
    {
        var requestBody = new
        {
            model = _settings.Model,
            max_tokens = _settings.MaxTokens,
            temperature = _settings.Temperature,
            system = systemPrompt,
            messages = new[]
            {
                new { role = "user", content = userMessage }
            }
        };

        var json = JsonSerializer.Serialize(requestBody, JsonOptions);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages");
        request.Content = content;
        request.Headers.Add("x-api-key", _settings.ApiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");

        _logger.LogDebug("Sending request to Claude API with model {Model}", _settings.Model);

        var response = await _httpClient.SendAsync(request, ct);

        var responseBody = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            _logger.LogError(
                "Claude API returned {StatusCode}: {Body}",
                response.StatusCode,
                responseBody);
            throw new HttpRequestException(
                $"Claude API request failed with status {response.StatusCode}: {responseBody}");
        }

        var claudeResponse = JsonSerializer.Deserialize<ClaudeResponse>(responseBody, JsonOptions);

        if (claudeResponse?.Content is null || claudeResponse.Content.Count == 0)
        {
            _logger.LogWarning("Claude API returned empty content");
            throw new InvalidOperationException("Claude API returned no content blocks.");
        }

        var textBlock = claudeResponse.Content.FirstOrDefault(c => c.Type == "text");
        if (textBlock is null)
        {
            throw new InvalidOperationException("Claude API returned no text content block.");
        }

        _logger.LogDebug(
            "Claude API response received. Input tokens: {Input}, Output tokens: {Output}",
            claudeResponse.InputTokens,
            claudeResponse.OutputTokens);

        return textBlock.Text;
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
                "Failed to deserialize Claude response to {Type}. Response: {Response}",
                typeof(T).Name,
                responseText);
            throw new InvalidOperationException(
                $"Failed to parse Claude response as {typeof(T).Name}: {ex.Message}", ex);
        }
    }

    private static string ExtractJson(string text)
    {
        var trimmed = text.Trim();

        // Strip markdown code blocks if present
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
}
