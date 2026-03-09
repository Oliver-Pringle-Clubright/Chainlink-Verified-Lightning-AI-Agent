namespace LightningAgent.Core.Models.Chainlink;

public class ChainlinkFunctionResponse
{
    public string RequestId { get; set; } = string.Empty;
    public byte[] Response { get; set; } = Array.Empty<byte>();
    public byte[] Error { get; set; } = Array.Empty<byte>();
    public string? TxHash { get; set; }
}
