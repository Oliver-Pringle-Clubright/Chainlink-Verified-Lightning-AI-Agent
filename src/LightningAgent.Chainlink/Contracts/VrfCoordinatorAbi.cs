namespace LightningAgent.Chainlink.Contracts;

public static class VrfCoordinatorAbi
{
    public const string Abi = @"[
        {""inputs"":[
            {""components"":[
                {""name"":""keyHash"",""type"":""bytes32""},
                {""name"":""subId"",""type"":""uint64""},
                {""name"":""requestConfirmations"",""type"":""uint16""},
                {""name"":""callbackGasLimit"",""type"":""uint32""},
                {""name"":""numWords"",""type"":""uint32""}
            ],""name"":""req"",""type"":""tuple""}
        ],""name"":""requestRandomWords"",""outputs"":[{""name"":""requestId"",""type"":""uint256""}],""stateMutability"":""nonpayable"",""type"":""function""}
    ]";
}
