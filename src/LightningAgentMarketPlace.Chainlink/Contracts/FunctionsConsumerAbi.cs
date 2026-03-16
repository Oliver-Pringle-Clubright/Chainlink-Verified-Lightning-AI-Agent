namespace LightningAgentMarketPlace.Chainlink.Contracts;

public static class FunctionsConsumerAbi
{
    public const string Abi = @"[
        {""inputs"":[
            {""name"":""source"",""type"":""string""},
            {""name"":""encryptedSecretsUrls"",""type"":""bytes""},
            {""name"":""donHostedSecretsSlotID"",""type"":""uint8""},
            {""name"":""donHostedSecretsVersion"",""type"":""uint64""},
            {""name"":""args"",""type"":""string[]""},
            {""name"":""bytesArgs"",""type"":""bytes[]""},
            {""name"":""subscriptionId"",""type"":""uint64""},
            {""name"":""gasLimit"",""type"":""uint32""},
            {""name"":""donID"",""type"":""bytes32""}
        ],""name"":""sendRequest"",""outputs"":[{""name"":""requestId"",""type"":""bytes32""}],""stateMutability"":""nonpayable"",""type"":""function""},
        {""inputs"":[],""name"":""s_lastResponse"",""outputs"":[{""name"":"""",""type"":""bytes""}],""stateMutability"":""view"",""type"":""function""},
        {""inputs"":[],""name"":""s_lastError"",""outputs"":[{""name"":"""",""type"":""bytes""}],""stateMutability"":""view"",""type"":""function""},
        {""inputs"":[],""name"":""s_lastRequestId"",""outputs"":[{""name"":"""",""type"":""bytes32""}],""stateMutability"":""view"",""type"":""function""}
    ]";
}
