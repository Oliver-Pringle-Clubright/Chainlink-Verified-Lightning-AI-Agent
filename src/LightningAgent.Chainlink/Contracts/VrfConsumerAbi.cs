namespace LightningAgent.Chainlink.Contracts;

/// <summary>
/// ABI for reading fulfilled VRF randomness from a VRFConsumerBaseV2Plus consumer contract.
/// The consumer stores the last request ID and random words after fulfillment.
/// </summary>
public static class VrfConsumerAbi
{
    public const string Abi = @"[
        {""inputs"":[],""name"":""s_lastRequestId"",""outputs"":[{""name"":"""",""type"":""uint256""}],""stateMutability"":""view"",""type"":""function""},
        {""inputs"":[{""name"":"""",""type"":""uint256""}],""name"":""s_requests"",""outputs"":[
            {""name"":""fulfilled"",""type"":""bool""},
            {""name"":""exists"",""type"":""bool""}
        ],""stateMutability"":""view"",""type"":""function""},
        {""inputs"":[{""name"":""_requestId"",""type"":""uint256""}],""name"":""getRequestStatus"",""outputs"":[
            {""name"":""fulfilled"",""type"":""bool""},
            {""name"":""randomWords"",""type"":""uint256[]""}
        ],""stateMutability"":""view"",""type"":""function""}
    ]";
}
