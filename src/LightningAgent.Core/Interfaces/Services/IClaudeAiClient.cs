namespace LightningAgent.Core.Interfaces.Services;

public interface IClaudeAiClient
{
    Task<string> SendMessageAsync(string systemPrompt, string userMessage, CancellationToken ct = default);
    Task<T> SendStructuredRequestAsync<T>(string systemPrompt, string userMessage, CancellationToken ct = default);
}
