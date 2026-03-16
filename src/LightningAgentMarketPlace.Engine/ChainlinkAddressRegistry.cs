using LightningAgentMarketPlace.Core.Configuration;

namespace LightningAgentMarketPlace.Engine;

/// <summary>
/// Static registry of Chainlink contract addresses per chain.
/// Addresses are sourced from the official Chainlink documentation and toolkit.
/// Only deployment-specific values (SubscriptionId, PrivateKeyPath, VrfConsumerAddress) must be configured manually.
/// </summary>
public static class ChainlinkAddressRegistry
{
    /// <summary>
    /// Returns default Chainlink addresses for the given chain ID, or null if unrecognized.
    /// </summary>
    public static ChainlinkNetworkConfig? GetDefaults(long chainId)
    {
        return chainId switch
        {
            // ── Ethereum Mainnet ─────────────────────────────────────
            1 => new ChainlinkNetworkConfig
            {
                BtcUsdPriceFeedAddress = "0xF4030086522a5bEEa4988F8cA5B36dbC97BeE88c",
                EthUsdPriceFeedAddress = "0x5f4eC3Df9cbd43714FE2740f5E3616155c5b8419",
                LinkUsdPriceFeedAddress = "0x2c1d072e956AFFC0D435Cb7AC38EF18d24d9127c",
                FunctionsRouterAddress = "0x65Dcc24F8ff9e51F10DCc7Ed1e4e2A61e6E14bd6",
                VrfCoordinatorAddress = "0xD7f86b4b8Cae7D942340FF628F82735b7a20893a",
                AutomationRegistryAddress = "0x6593c7De001fC8542bB1703532EE1E5aA0D458fD",
                CcipRouterAddress = "0x80226fc0Ee2b096224EeAc085Bb9a8cba1146f7D",
                CcipSourceChainSelector = 5009297550715157269,
                DonId = "fun-ethereum-mainnet-1"
            },

            // ── Ethereum Sepolia ─────────────────────────────────────
            11155111 => new ChainlinkNetworkConfig
            {
                BtcUsdPriceFeedAddress = "0x1b44F3514812d835EB1BDB0acB33d3fA3351Ee43",
                EthUsdPriceFeedAddress = "0x694AA1769357215DE4FAC081bf1f309aDC325306",
                LinkUsdPriceFeedAddress = "0xc59E3633BAAC79493d908e63626716e204A45EdF",
                FunctionsRouterAddress = "0xb83E47C2bC239B3bf370bc41e1459A34b41238D0",
                VrfCoordinatorAddress = "0x9DdfaCa8183c41ad55329BdeeD9F6A8d53168B1B",
                AutomationRegistryAddress = "0x86EFBD0b6736Bed994962f9797049422A3A8E8Ad",
                CcipRouterAddress = "0x0BF3dE8c5D3e8A2B34D2BEeB17ABfCeBaf363A59",
                CcipSourceChainSelector = 16015286601757825753,
                DonId = "fun-ethereum-sepolia-1"
            },

            // ── Arbitrum One ─────────────────────────────────────────
            42161 => new ChainlinkNetworkConfig
            {
                EthUsdPriceFeedAddress = "0x639Fe6ab55C921f74e7fac1ee960C0B6293ba612",
                FunctionsRouterAddress = "0x97083e831f8f0638855e2a515c90edcf158df238",
                VrfCoordinatorAddress = "0x3C0Ca683b403E37668AE3DC4FB62F4B29B6f7a3e",
                CcipRouterAddress = "0x141fa059441E0ca23ce184B6A78bafD2A517DdE8",
                CcipSourceChainSelector = 4949039107694359620,
                DonId = "fun-arbitrum-mainnet-1"
            },

            // ── Arbitrum Sepolia ─────────────────────────────────────
            421614 => new ChainlinkNetworkConfig
            {
                EthUsdPriceFeedAddress = "0xd30e2101a97dcbAeBCBC04F14C3f624E67A35165",
                FunctionsRouterAddress = "0x234a5fb5Bd614a7AA2FfAB244D603abFA0Ac5C5C",
                VrfCoordinatorAddress = "0x5CE8D5A2BC84beb22a398CCA51996F7930313D61",
                CcipRouterAddress = "0x2a9C5afB0d0e4BAb2BCdaE109EC4b0c4Be15a165",
                CcipSourceChainSelector = 3478487238524512106,
                DonId = "fun-arbitrum-sepolia-1"
            },

            // ── Base ─────────────────────────────────────────────────
            8453 => new ChainlinkNetworkConfig
            {
                EthUsdPriceFeedAddress = "0x71041dddad3595F9CEd3DcCFBe3D1F4b0a16Bb70",
                FunctionsRouterAddress = "0xf9b8fc078197181c841c296c876945aaa425b278",
                VrfCoordinatorAddress = "0xd5D517aBE5cF79B7e95eC98dB0f0277788aFF634",
                CcipRouterAddress = "0x881e3A65B4d4a04310c4f4b2B1b7E9528b73e2F6",
                CcipSourceChainSelector = 15971525489660198786,
                DonId = "fun-base-mainnet-1"
            },

            // ── Base Sepolia ─────────────────────────────────────────
            84532 => new ChainlinkNetworkConfig
            {
                EthUsdPriceFeedAddress = "0x4aDC67696bA383F43DD60A9e78F2C97Fbbfc7cb1",
                FunctionsRouterAddress = "0xf9B8fc078197181C841c296C876945aaa425B278",
                VrfCoordinatorAddress = "0x5C210eF41CD1a72de73bF76eC39637bB0d3d7BEE",
                CcipRouterAddress = "0xD3b06cEbF099CE7DA4AcCf578aaebFDBd6e88a93",
                CcipSourceChainSelector = 10344971235874465080,
                DonId = "fun-base-sepolia-1"
            },

            // ── Polygon ──────────────────────────────────────────────
            137 => new ChainlinkNetworkConfig
            {
                EthUsdPriceFeedAddress = "0xF9680D99D6C9589e2a93a78A04A279e509205945",
                FunctionsRouterAddress = "0xdc2AAF042Aeff2E68B3e8E33F19e4B9fA7C73F10",
                VrfCoordinatorAddress = "0xec0Ed46f36576541C75739E915ADbCb3DE24bD77",
                CcipRouterAddress = "0x849c5ED5a80F5B408Dd4969b78c2C8fdf0565Bfe",
                CcipSourceChainSelector = 4051577828743386545,
                DonId = "fun-polygon-mainnet-1"
            },

            // ── Polygon Amoy ─────────────────────────────────────────
            80002 => new ChainlinkNetworkConfig
            {
                FunctionsRouterAddress = "0xC22a79eBA640940ABB6dF0f7982cc119578E11De",
                VrfCoordinatorAddress = "0x343300b5d84D444B2ADc9116FEF1bED02BE49Cf2",
                CcipRouterAddress = "0x9C32fCB86BF0f4a1A8921a9Fe46de3198bb884B2",
                CcipSourceChainSelector = 16281711391670634445,
                DonId = "fun-polygon-amoy-1"
            },

            // ── BNB Smart Chain ──────────────────────────────────────
            56 => new ChainlinkNetworkConfig
            {
                EthUsdPriceFeedAddress = "0x9ef1B8c0E4F7dc8bF5719Ea496883DC6401d5b2e",
                VrfCoordinatorAddress = "0xc587d9053cd1118f25F645F9E08BB98c9712A4EE",
                CcipRouterAddress = "0x34B03Cb9086d7D758AC55af71584F81A598759FE",
                CcipSourceChainSelector = 11344663589394136015
                // Note: Chainlink Functions is NOT available on BNB Chain
            },

            // ── BNB Testnet ──────────────────────────────────────────
            97 => new ChainlinkNetworkConfig
            {
                VrfCoordinatorAddress = "0x765162538b37BF1Af08DfC2e14c86768E6C3b8A6",
                CcipRouterAddress = "0xE1053aE1857476f36A3C62580FF9b016E8EE8F6f",
                CcipSourceChainSelector = 13264668187771770619
            },

            // ── Optimism ─────────────────────────────────────────────
            10 => new ChainlinkNetworkConfig
            {
                EthUsdPriceFeedAddress = "0x13e3Ee699D1909E989722E753853AE30b17e08c5",
                FunctionsRouterAddress = "0xaA8AaA682C9eF150C0C8E96a8D60945BCB21faad",
                VrfCoordinatorAddress = "0x5FE58960F730153eb5A84a47C51BD4E58302E1c8",
                CcipRouterAddress = "0x3206695CaE29952f4b0c22a169725a865bc8Ce0f",
                CcipSourceChainSelector = 3734403246176062136,
                DonId = "fun-optimism-mainnet-1"
            },

            // ── Optimism Sepolia ─────────────────────────────────────
            11155420 => new ChainlinkNetworkConfig
            {
                EthUsdPriceFeedAddress = "0x61Ec26aA57019C486B10502285c5A3D4A4750AD7",
                FunctionsRouterAddress = "0xC17094E3A1348E5C7544D4fF8A36c28f2C6AAE28",
                VrfCoordinatorAddress = "0x02667f44a6a44E4BDddCF80e724512Ad3426B17d",
                CcipRouterAddress = "0x114A20A10b43D4115e5aeef7345a1A71d2a60C57",
                CcipSourceChainSelector = 5224473277236331295,
                DonId = "fun-optimism-sepolia-1"
            },

            // ── Avalanche ────────────────────────────────────────────
            43114 => new ChainlinkNetworkConfig
            {
                FunctionsRouterAddress = "0x9f82a6A0758517FD0AfA463820F586999AF314a0",
                VrfCoordinatorAddress = "0xE40895D055bccd2053dD0638C9695E326152b1A4",
                CcipRouterAddress = "0xF4c7E640EdA248ef95972845a62bdC74237805dB",
                CcipSourceChainSelector = 6433500567565415381,
                DonId = "fun-avalanche-mainnet-1"
            },

            // ── Avalanche Fuji ───────────────────────────────────────
            43113 => new ChainlinkNetworkConfig
            {
                FunctionsRouterAddress = "0xA9d587a00A31A52Ed70D6026794a8FC5E2F5dCb0",
                VrfCoordinatorAddress = "0x5C210eF41CD1a72de73bF76eC39637bB0d3d7BEE",
                CcipRouterAddress = "0xF694E193200268f9a4868e4Aa017A0118C9a8177",
                CcipSourceChainSelector = 14767482510784806043,
                DonId = "fun-avalanche-fuji-1"
            },

            _ => null
        };
    }

    /// <summary>
    /// Returns true if the chain ID is a known mainnet chain.
    /// </summary>
    public static bool IsMainnet(long chainId) =>
        chainId is 1 or 56 or 137 or 42161 or 10 or 8453 or 43114;

    /// <summary>
    /// Returns a human-readable name for the chain.
    /// </summary>
    public static string GetChainName(long chainId) => chainId switch
    {
        1 => "Ethereum",
        11155111 => "Ethereum Sepolia",
        56 => "BNB Smart Chain",
        97 => "BNB Testnet",
        137 => "Polygon",
        80002 => "Polygon Amoy",
        42161 => "Arbitrum One",
        421614 => "Arbitrum Sepolia",
        10 => "Optimism",
        11155420 => "Optimism Sepolia",
        8453 => "Base",
        84532 => "Base Sepolia",
        43114 => "Avalanche",
        43113 => "Avalanche Fuji",
        _ => $"Unknown (Chain {chainId})"
    };

    /// <summary>
    /// Copies non-empty address fields from registry defaults into the target config,
    /// only filling in fields that are currently empty.
    /// Does NOT copy deployment-specific fields (SubscriptionId, PrivateKeyPath, VrfConsumerAddress).
    /// </summary>
    public static void ApplyDefaults(ChainlinkNetworkConfig target, ChainlinkNetworkConfig defaults)
    {
        if (string.IsNullOrEmpty(target.EthUsdPriceFeedAddress) && !string.IsNullOrEmpty(defaults.EthUsdPriceFeedAddress))
            target.EthUsdPriceFeedAddress = defaults.EthUsdPriceFeedAddress;
        if (string.IsNullOrEmpty(target.BtcUsdPriceFeedAddress) && !string.IsNullOrEmpty(defaults.BtcUsdPriceFeedAddress))
            target.BtcUsdPriceFeedAddress = defaults.BtcUsdPriceFeedAddress;
        if (string.IsNullOrEmpty(target.LinkUsdPriceFeedAddress) && !string.IsNullOrEmpty(defaults.LinkUsdPriceFeedAddress))
            target.LinkUsdPriceFeedAddress = defaults.LinkUsdPriceFeedAddress;
        if (string.IsNullOrEmpty(target.LinkEthPriceFeedAddress) && !string.IsNullOrEmpty(defaults.LinkEthPriceFeedAddress))
            target.LinkEthPriceFeedAddress = defaults.LinkEthPriceFeedAddress;
        if (string.IsNullOrEmpty(target.FunctionsRouterAddress) && !string.IsNullOrEmpty(defaults.FunctionsRouterAddress))
            target.FunctionsRouterAddress = defaults.FunctionsRouterAddress;
        if (string.IsNullOrEmpty(target.VrfCoordinatorAddress) && !string.IsNullOrEmpty(defaults.VrfCoordinatorAddress))
            target.VrfCoordinatorAddress = defaults.VrfCoordinatorAddress;
        if (string.IsNullOrEmpty(target.AutomationRegistryAddress) && !string.IsNullOrEmpty(defaults.AutomationRegistryAddress))
            target.AutomationRegistryAddress = defaults.AutomationRegistryAddress;
        if (string.IsNullOrEmpty(target.CcipRouterAddress) && !string.IsNullOrEmpty(defaults.CcipRouterAddress))
            target.CcipRouterAddress = defaults.CcipRouterAddress;
        if (target.CcipSourceChainSelector == 0 && defaults.CcipSourceChainSelector != 0)
            target.CcipSourceChainSelector = defaults.CcipSourceChainSelector;
        if (string.IsNullOrEmpty(target.DonId) && !string.IsNullOrEmpty(defaults.DonId))
            target.DonId = defaults.DonId;
        if (string.IsNullOrEmpty(target.VrfKeyHash) && !string.IsNullOrEmpty(defaults.VrfKeyHash))
            target.VrfKeyHash = defaults.VrfKeyHash;
    }
}
