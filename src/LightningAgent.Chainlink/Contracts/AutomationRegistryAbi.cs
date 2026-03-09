namespace LightningAgent.Chainlink.Contracts;

public static class AutomationRegistryAbi
{
    public const string Abi = @"[
        {""inputs"":[{""name"":""id"",""type"":""uint256""}],""name"":""getUpkeep"",""outputs"":[
            {""name"":""target"",""type"":""address""},
            {""name"":""executeGas"",""type"":""uint32""},
            {""name"":""checkData"",""type"":""bytes""},
            {""name"":""balance"",""type"":""uint96""},
            {""name"":""admin"",""type"":""address""},
            {""name"":""maxValidBlocknumber"",""type"":""uint64""},
            {""name"":""lastPerformBlockNumber"",""type"":""uint32""},
            {""name"":""amountSpent"",""type"":""uint96""},
            {""name"":""paused"",""type"":""bool""}
        ],""stateMutability"":""view"",""type"":""function""},
        {""inputs"":[{""name"":""id"",""type"":""uint256""}],""name"":""cancelUpkeep"",""outputs"":[],""stateMutability"":""nonpayable"",""type"":""function""},
        {""inputs"":[
            {""name"":""target"",""type"":""address""},
            {""name"":""gasLimit"",""type"":""uint32""},
            {""name"":""admin"",""type"":""address""},
            {""name"":""checkData"",""type"":""bytes""}
        ],""name"":""registerUpkeep"",""outputs"":[{""name"":""id"",""type"":""uint256""}],""stateMutability"":""nonpayable"",""type"":""function""}
    ]";
}
