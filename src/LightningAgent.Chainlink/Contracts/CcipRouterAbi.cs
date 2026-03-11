namespace LightningAgent.Chainlink.Contracts;

/// <summary>
/// ABI for the Chainlink CCIP Router contract (IRouterClient).
/// Sepolia router: 0x0BF3dE8c5D3e8A2B34D2BEeB17ABfCeBaf363A59
/// </summary>
public static class CcipRouterAbi
{
    public const string Abi = @"[
        {""inputs"":[{""name"":""destinationChainSelector"",""type"":""uint64""}],""name"":""isChainSupported"",""outputs"":[{""name"":"""",""type"":""bool""}],""stateMutability"":""view"",""type"":""function""},
        {""inputs"":[
            {""name"":""destinationChainSelector"",""type"":""uint64""},
            {""components"":[
                {""name"":""receiver"",""type"":""bytes""},
                {""name"":""data"",""type"":""bytes""},
                {""components"":[
                    {""name"":""token"",""type"":""address""},
                    {""name"":""amount"",""type"":""uint256""}
                ],""name"":""tokenAmounts"",""type"":""tuple[]""},
                {""name"":""feeToken"",""type"":""address""},
                {""name"":""extraArgs"",""type"":""bytes""}
            ],""name"":""message"",""type"":""tuple""}
        ],""name"":""getFee"",""outputs"":[{""name"":""fee"",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
        {""inputs"":[
            {""name"":""destinationChainSelector"",""type"":""uint64""},
            {""components"":[
                {""name"":""receiver"",""type"":""bytes""},
                {""name"":""data"",""type"":""bytes""},
                {""components"":[
                    {""name"":""token"",""type"":""address""},
                    {""name"":""amount"",""type"":""uint256""}
                ],""name"":""tokenAmounts"",""type"":""tuple[]""},
                {""name"":""feeToken"",""type"":""address""},
                {""name"":""extraArgs"",""type"":""bytes""}
            ],""name"":""message"",""type"":""tuple""}
        ],""name"":""ccipSend"",""outputs"":[{""name"":""messageId"",""type"":""bytes32""}],""stateMutability"":""payable"",""type"":""function""}
    ]";

    /// <summary>
    /// ABI for the CCIP OnRamp contract to query sent message status.
    /// </summary>
    public const string OnRampEventAbi = @"[
        {""anonymous"":false,""inputs"":[
            {""indexed"":true,""name"":""messageId"",""type"":""bytes32""},
            {""indexed"":false,""name"":""sequenceNumber"",""type"":""uint64""},
            {""indexed"":false,""name"":""message"",""type"":""bytes""},
            {""indexed"":false,""name"":""tokenAmounts"",""type"":""uint256[]""},
            {""indexed"":false,""name"":""feeTokenAmount"",""type"":""uint256""}
        ],""name"":""CCIPSendRequested"",""type"":""event""}
    ]";
}
