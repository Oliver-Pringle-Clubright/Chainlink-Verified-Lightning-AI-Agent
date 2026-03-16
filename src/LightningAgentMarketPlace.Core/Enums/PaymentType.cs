namespace LightningAgentMarketPlace.Core.Enums;

public enum PaymentType
{
    Escrow,
    Streaming,
    Direct,
    OnChainBtc,
    Erc20,
    NativeToken,
    LinkToken,
    CcipTransfer
}

public enum PaymentMethod
{
    Lightning,
    OnChainBtc,
    Erc20Usdc,
    Erc20Usdt,
    Erc20Link,
    NativeEth,
    NativeMatic,
    NativeBnb,
    NativeAvax,
    CcipBridge
}
