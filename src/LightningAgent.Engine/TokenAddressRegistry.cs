namespace LightningAgent.Engine;

/// <summary>
/// Registry of well-known ERC-20 token addresses per chain.
/// Addresses sourced from official token deployments.
/// </summary>
public static class TokenAddressRegistry
{
    public static string? GetUsdcAddress(long chainId) => chainId switch
    {
        1 => "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48",       // Ethereum
        42161 => "0xaf88d065e77c8cC2239327C5EDb3A432268e5831",     // Arbitrum One
        8453 => "0x833589fCD6eDb6E08f4c7C32D4f71b54bdA02913",      // Base
        137 => "0x3c499c542cEF5E3811e1192ce70d8cC03d5c3359",       // Polygon
        56 => "0x8AC76a51cc950d9822D68b83fE1Ad97B32Cd580d",        // BNB Chain
        10 => "0x0b2C639c533813f4Aa9D7837CAf62653d097Ff85",        // Optimism
        43114 => "0xB97EF9Ef8734C71904D8002F8b6Bc66Dd9c48a6E",     // Avalanche
        11155111 => "0x1c7D4B196Cb0C7B01d743Fbc6116a902379C7238",  // Sepolia
        _ => null
    };

    public static string? GetUsdtAddress(long chainId) => chainId switch
    {
        1 => "0xdAC17F958D2ee523a2206206994597C13D831ec7",       // Ethereum
        42161 => "0xFd086bC7CD5C481DCC9C85ebE478A1C0b69FCbb9",   // Arbitrum One
        137 => "0xc2132D05D31c914a87C6611C10748AEb04B58e8F",     // Polygon
        56 => "0x55d398326f99059fF775485246999027B3197955",      // BNB Chain
        10 => "0x94b008aA00579c1307B0EF2c499aD98a8ce58e58",     // Optimism
        43114 => "0x9702230A8Ea53601f5cD2dc00fDBc13d4dF4A8c7",   // Avalanche
        _ => null
    };

    public static string? GetLinkAddress(long chainId) => chainId switch
    {
        1 => "0x514910771AF9Ca656af840dff83E8264EcF986CA",       // Ethereum
        42161 => "0xf97f4df75117a78c1A5a0DBb814Af92458539FB4",   // Arbitrum One
        8453 => "0x88Fb150BDc53A65fe94Dea0c9BA0a6dAf8C6e196",    // Base
        137 => "0xb0897686c545045aFc77CF20eC7A532E3120E0F1",     // Polygon
        56 => "0x404460C6A5EdE2D891e8297795264fDe62ADBB75",      // BNB Chain (PeggedLINK)
        10 => "0x350a791Bfc2C21F9Ed5d10980Dad2e2638ffa7f6",      // Optimism
        43114 => "0x5947BB275c521040051D82396192181b413227A3",    // Avalanche
        11155111 => "0x779877A7B0D9E8603169DdbD7836e478b4624789", // Sepolia
        _ => null
    };

    /// <summary>
    /// Returns the native currency symbol for a chain.
    /// </summary>
    public static string GetNativeCurrency(long chainId) => chainId switch
    {
        1 or 11155111 or 42161 or 421614 or 8453 or 84532 or 10 or 11155420 => "ETH",
        137 or 80002 => "MATIC",
        56 or 97 => "BNB",
        43114 or 43113 => "AVAX",
        _ => "ETH"
    };

    /// <summary>
    /// Standard ERC-20 ABI for transfer and balanceOf.
    /// </summary>
    public const string Erc20TransferAbi = @"[
        {""constant"":false,""inputs"":[{""name"":""to"",""type"":""address""},{""name"":""value"",""type"":""uint256""}],""name"":""transfer"",""outputs"":[{""name"":"""",""type"":""bool""}],""type"":""function""},
        {""constant"":true,""inputs"":[{""name"":""owner"",""type"":""address""}],""name"":""balanceOf"",""outputs"":[{""name"":"""",""type"":""uint256""}],""type"":""function""},
        {""constant"":true,""inputs"":[],""name"":""decimals"",""outputs"":[{""name"":"""",""type"":""uint8""}],""type"":""function""}
    ]";
}
