using LightningAgent.Core.Enums;
using LightningAgent.Core.Models;

namespace LightningAgent.Core.Interfaces.Services;

/// <summary>
/// Abstraction for a payment provider that can send payments via different methods.
/// </summary>
public interface IPaymentProvider
{
    PaymentMethod Method { get; }
    bool IsAvailable { get; }
    Task<PaymentResult> SendPaymentAsync(PaymentRequest request, CancellationToken ct = default);
}

public record PaymentRequest
{
    public long AmountSats { get; init; }
    public double? AmountUsd { get; init; }
    public string ReceiverAddress { get; init; } = "";
    public long? ChainId { get; init; }
    public string? TokenAddress { get; init; }
    public string? Memo { get; init; }
    public int TaskId { get; init; }
    public int? MilestoneId { get; init; }
    public int AgentId { get; init; }
}

public record PaymentResult
{
    public bool Success { get; init; }
    public string? TransactionHash { get; init; }
    public string? PaymentHash { get; init; }
    public string? SenderAddress { get; init; }
    public string? AmountWei { get; init; }
    public string? Error { get; init; }
    public PaymentType PaymentType { get; init; }
    public PaymentMethod PaymentMethod { get; init; }
    public long? ChainId { get; init; }
    public string? TokenAddress { get; init; }
}
