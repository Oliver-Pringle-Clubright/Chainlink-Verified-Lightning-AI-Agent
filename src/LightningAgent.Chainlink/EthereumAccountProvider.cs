using Nethereum.Web3.Accounts;

namespace LightningAgent.Chainlink;

public static class EthereumAccountProvider
{
    public static Account? CreateAccount(string privateKeyPath)
    {
        if (string.IsNullOrWhiteSpace(privateKeyPath))
            return null;

        var privateKey = File.ReadAllText(privateKeyPath).Trim();
        return new Account(privateKey);
    }
}
