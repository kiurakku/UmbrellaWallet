using System.Numerics;
using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap N.1 — sending an arbitrary ERC-20, and the two ways that goes wrong quietly.
///
/// <b>The ticker is not the token.</b> Two contracts can call themselves USDC, and a picker keyed on
/// the ticker would eventually build a transfer against whichever one it found first. So the routing
/// key is the contract, and these pin that it stays that way.
///
/// <b>The decimals are not a default.</b> USDT has 6, most tokens have 18. A row whose decimals were
/// never read must be refused rather than assumed: sending "1" with the wrong value is off by a
/// factor of a trillion, and in one direction it is the entire balance.
/// </summary>
public sealed class TokenSendRoutingTests
{
    private const string Usdc = "0xA0b86991c6218b36c1d19D4a2e9Eb0cE3606eB48";
    private const string Link = "0x514910771AF9Ca656af840dff83E8264EcF986CA";

    private static WalletAccountViewModel TokenRow(
        string symbol, string contract, int decimals, double amount = 10, bool spam = false) =>
        new(symbol, $"{symbol} · ERC20", "Ready", "0xE6dC000000000000000000000000000000004B04",
            "ERC20 on Ethereum", Price: 1, Amount: amount, Chain: "Ethereum", Change24h: 0,
            IsSuspectedSpam: spam, Balance: Umbrella.Wallet.App.BalanceRead.Live,
            Contract: contract, TokenDecimals: decimals);

    [Fact]
    public void A_token_send_key_carries_the_contract_not_the_ticker()
    {
        var key = MainViewModel.TokenSendPrefix + Usdc;

        Assert.Equal(Usdc, MainViewModel.ContractFromSendKey(key));

        // A native coin's key is just its ticker, and must not be mistaken for a token.
        Assert.Null(MainViewModel.ContractFromSendKey("ETH"));
        Assert.Null(MainViewModel.ContractFromSendKey("BTC"));
        Assert.Null(MainViewModel.ContractFromSendKey(null));
    }

    [Fact]
    public void Two_tokens_sharing_a_ticker_stay_distinguishable()
    {
        // The impersonation case: a fake "USDC" with a different contract. Their keys must differ,
        // or the send path has no way to tell which one the user picked.
        var real = MainViewModel.TokenSendPrefix + Usdc;
        var fake = MainViewModel.TokenSendPrefix + Link;

        Assert.NotEqual(real, fake);
        Assert.NotEqual(MainViewModel.ContractFromSendKey(real), MainViewModel.ContractFromSendKey(fake));
    }

    [Fact]
    public void A_row_without_decimals_is_not_spendable()
    {
        // -1 is "the contract never told us". Assuming 18 here is the classic way to send a
        // thousand times too much, or a millionth of what was meant.
        Assert.False(TokenRow("MYSTERY", Link, decimals: -1).IsSpendableToken);
        Assert.True(TokenRow("LINK", Link, decimals: 18).IsSpendableToken);
    }

    [Fact]
    public void A_row_without_a_contract_is_not_spendable()
    {
        Assert.False(TokenRow("LINK", contract: "", decimals: 18).IsSpendableToken);
    }

    /// <summary>
    /// The picker shows the ticker; it must never show the routing key, which is a 42-character hex
    /// string nobody recognises as their money.
    /// </summary>
    [Fact]
    public void The_picker_shows_the_ticker_and_routes_on_the_contract()
    {
        var option = new SendOption(
            MainViewModel.TokenSendPrefix + Usdc, "USD Coin", "Ethereum · ERC-20", Ticker: "USDC");

        Assert.Equal("USDC", option.DisplayTicker);
        Assert.Equal("USDC · USD Coin", option.Display);
        Assert.StartsWith(MainViewModel.TokenSendPrefix, option.Symbol, StringComparison.Ordinal);
    }

    [Fact]
    public void A_native_option_still_displays_as_it_always_did()
    {
        var eth = new SendOption("ETH", "Ethereum", "Ethereum network");

        Assert.Equal("ETH", eth.DisplayTicker);
        Assert.Equal("ETH · Ethereum", eth.Display);
    }

    /// <summary>
    /// The calldata is where the money actually is: the transaction's own value is zero, and the
    /// recipient and amount live in 68 bytes of hex. This pins both, for a 6-decimal token, against
    /// the ERC-20 standard's own layout rather than against this code's output.
    /// </summary>
    [Fact]
    public void The_transfer_calldata_encodes_the_recipient_and_the_scaled_amount()
    {
        const string recipient = "0x742d35Cc6634C0532925a3b844Bc454e4438f44e";
        var data = Erc20Transfer.EncodeCallData(recipient, 2.5m, decimals: 6);

        // selector + 32-byte address + 32-byte amount = 4 + 32 + 32 bytes, hex, with 0x.
        Assert.Equal(2 + 8 + 64 + 64, data.Length);
        Assert.StartsWith("0x" + Erc20Transfer.TransferSelector, data, StringComparison.Ordinal);
        Assert.Contains(recipient[2..].ToLowerInvariant(), data, StringComparison.Ordinal);

        // 2.5 USDC is 2_500_000 base units — not 2.5, and not 2.5e18.
        var amountHex = data[^64..].TrimStart('0');
        Assert.Equal(new BigInteger(2_500_000), BigInteger.Parse("0" + amountHex, System.Globalization.NumberStyles.HexNumber));
    }

    /// <summary>
    /// The wallet asks the contract what it holds rather than trusting a cached row, so the calldata
    /// for that question has to be right too.
    /// </summary>
    [Fact]
    public void The_balance_query_encodes_the_owner()
    {
        const string owner = "0xE6dC000000000000000000000000000000004B04";
        var data = Erc20Transfer.EncodeBalanceOf(owner);

        Assert.Equal("0x" + Erc20Transfer.BalanceOfSelector + owner[2..].ToLowerInvariant().PadLeft(64, '0'), data);
    }
}
