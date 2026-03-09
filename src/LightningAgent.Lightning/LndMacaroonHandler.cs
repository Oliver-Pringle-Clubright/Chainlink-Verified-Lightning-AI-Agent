namespace LightningAgent.Lightning;

/// <summary>
/// A delegating handler that attaches the LND macaroon authentication header to every outgoing request.
/// </summary>
internal sealed class LndMacaroonHandler : DelegatingHandler
{
    private readonly string _macaroonPath;
    private string? _macaroonHex;

    public LndMacaroonHandler(string macaroonPath)
    {
        _macaroonPath = macaroonPath;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (_macaroonHex is null && !string.IsNullOrEmpty(_macaroonPath))
        {
            var macaroonBytes = await File.ReadAllBytesAsync(_macaroonPath, cancellationToken);
            _macaroonHex = Convert.ToHexString(macaroonBytes).ToLowerInvariant();
        }

        if (_macaroonHex is not null)
        {
            request.Headers.Remove("Grpc-Metadata-macaroon");
            request.Headers.Add("Grpc-Metadata-macaroon", _macaroonHex);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}
