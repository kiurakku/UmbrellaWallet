using NBitcoin;
using NBitcoin.Altcoins;
using Nethereum.Util;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;
using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Core.Tests;

public class ChainCatalogTests
{
    [Fact]
    public void Catalog_marks_supported_and_planned_chains_explicitly()
    {
        // BCH and ZEC are Supported for receive + balance only (real BIP44 address + live Blockchair
        // balance); their send paths are intentionally still gated behind CanSend:false. ZEC is
        // transparent-only (t-addr) — the public side of Zcash, not shielded. XRP (N.4) joins on the
        // same terms: receive + balance, CanSend:false until a signed Payment is proven.
        Assert.Equal(
            [ChainId.Btc, ChainId.Eth, ChainId.Ltc, ChainId.Doge, ChainId.Tron, ChainId.Sol, ChainId.Ton, ChainId.Ada, ChainId.Bch, ChainId.Zec, ChainId.Xrp, ChainId.Xlm, ChainId.Atom, ChainId.Near],
            ChainCatalog.Supported.Select(c => c.Id).ToArray());

        // Every chain now derives a real address; nothing is left "planned".
        Assert.Empty(ChainCatalog.Planned);

        // TON (v4R2) and now Cardano (CIP-1852 / BIP32-Ed25519) both send too — each transfer is pinned
        // byte-for-byte against its reference library. Monero alone stays receive-only here.
        Assert.Equal(
            [ChainId.Xmr],
            ChainCatalog.ReceiveOnly.Select(c => c.Id).ToArray());

        Assert.All(ChainCatalog.Supported, c =>
        {
            Assert.Equal(ChainSupportLevel.Supported, c.Support);
            Assert.False(string.IsNullOrWhiteSpace(c.DerivationScheme));
            Assert.False(string.IsNullOrWhiteSpace(c.ReceivePathTemplate));
        });

        Assert.All(ChainCatalog.Planned, c =>
        {
            Assert.Equal(ChainSupportLevel.Planned, c.Support);
            Assert.Null(c.DerivationScheme);
            Assert.Null(c.ReceivePathTemplate);
        });
    }
}

public class Bip39MnemonicServiceTests
{
    private readonly Bip39MnemonicService _sut = new();

    public const string FixedTwentyFourWordMnemonic =
        "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon art";

    [Fact]
    public void Generate_returns_valid_twenty_four_word_english_mnemonic()
    {
        var mnemonic = _sut.Generate();
        var words = mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(Bip39MnemonicService.GeneratedWordCount, words.Length);

        var result = _sut.Validate(mnemonic);
        Assert.True(result.IsValid);
        Assert.Equal(mnemonic, result.NormalizedMnemonic);
    }

    [Fact]
    public void Validate_accepts_fixed_twenty_four_word_mnemonic()
    {
        var result = _sut.Validate("  " + FixedTwentyFourWordMnemonic.ToUpperInvariant() + "  ");

        Assert.True(result.IsValid);
        Assert.Equal(FixedTwentyFourWordMnemonic, result.NormalizedMnemonic);
    }

    [Fact]
    public void Validate_accepts_valid_twelve_word_mnemonic()
    {
        const string twelve =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

        var result = _sut.Validate(twelve);

        Assert.True(result.IsValid);
        Assert.Equal(twelve, result.NormalizedMnemonic);
    }

    [Fact]
    public void Validate_rejects_invalid_word_count()
    {
        const string thirteen =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";

        var result = _sut.Validate(thirteen);

        Assert.False(result.IsValid);
        Assert.Contains("12, 15, 18, 21, or 24", result.Error);
    }

    [Fact]
    public void Validate_rejects_invalid_checksum()
    {
        var words = FixedTwentyFourWordMnemonic.Split(' ').ToArray();
        words[^1] = "zoo";
        var invalid = string.Join(' ', words);

        var result = _sut.Validate(invalid);

        Assert.False(result.IsValid);
        Assert.NotNull(result.Error);
    }

    /// <summary>Wallets present phrases in many shapes on export — numbered lists, commas, line breaks.
    /// A valid phrase must import regardless of that surrounding punctuation.</summary>
    [Fact]
    public void Validate_tolerates_numbered_and_punctuated_paste()
    {
        const string messy =
            "1. abandon  2. abandon\n3. abandon,4. abandon 5. abandon 6. abandon " +
            "7. abandon 8. abandon 9. abandon 10. abandon 11. abandon 12. about";

        var result = _sut.Validate(messy);

        Assert.True(result.IsValid);
        Assert.Equal(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about",
            result.NormalizedMnemonic);
    }

    /// <summary>A single mistyped word is the usual cause of a rejected phrase — name it, don't just
    /// say "invalid".</summary>
    [Fact]
    public void Validate_names_the_offending_word()
    {
        const string typo =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon aboutx";

        var result = _sut.Validate(typo);

        Assert.False(result.IsValid);
        Assert.Contains("aboutx", result.Error);
    }

    /// <summary>All-valid words but a failing checksum: the message must be specific (a typo / wrong
    /// order) and reference that TON wallets import fine, rather than a bare "invalid".</summary>
    [Fact]
    public void Validate_explains_checksum_failure_helpfully()
    {
        const string allValidBadChecksum =
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon " +
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon";

        var result = _sut.Validate(allValidBadChecksum);

        Assert.False(result.IsValid);
        Assert.Contains("checksum", result.Error);
        Assert.Contains("TON", result.Error);
    }
}

public class HdAddressDeriverTests
{
    private readonly HdAddressDeriver _sut = new();

    // Fixed BIP39 vector (24× abandon + art). Expected values produced by this core
    // via NBitcoin / NBitcoin.Altcoins / Nethereum.Util — asserted for determinism.
    private const string Mnemonic = Bip39MnemonicServiceTests.FixedTwentyFourWordMnemonic;

    public static TheoryData<ChainId, string, string> ExpectedIndexZero { get; } = new()
    {
        { ChainId.Btc, "m/84'/0'/0'/0/0", "bc1qzmtrqsfuaf6l6kkcsseumq26ukaphfj9skkug6" },
        { ChainId.Eth, "m/44'/60'/0'/0/0", "0xF278cF59F82eDcf871d630F28EcC8056f25C1cdb" },
        { ChainId.Ltc, "m/84'/2'/0'/0/0", "ltc1qj0xmcw3ttxgsfhzzcft9ac9nwp8smzq778lu3c" },
        { ChainId.Doge, "m/44'/3'/0'/0/0", "DL1DoPj4HvpnRT9n3YfCkhHXe5287wMyWD" },
        { ChainId.Tron, "m/44'/195'/0'/0/0", "TEfhiqsW1SdN44DeHrAWVmbyr8ZbvChrtS" },
        // SLIP-0010 ed25519 (proven against the official vectors in Slip10Ed25519Tests);
        // Phantom / solana-keygen use exactly this path + base58(pubkey).
        { ChainId.Sol, "m/44'/501'/0'/0'", "3Cy3YNTFywCmxoxt8n7UH6hg6dLo5uACowX3CFceaSnx" },
    };

    [Theory]
    [MemberData(nameof(ExpectedIndexZero))]
    public void DeriveReceiveAddress_is_deterministic_for_fixed_mnemonic(
        ChainId chain,
        string expectedPath,
        string expectedAddress)
    {
        var first = _sut.DeriveReceiveAddress(Mnemonic, chain, 0);
        var second = _sut.DeriveReceiveAddress(Mnemonic, chain, 0);

        Assert.Equal(expectedPath, first.DerivationPath);
        Assert.Equal(expectedAddress, first.Address);
        Assert.Equal(first, second);
    }

    [Theory]
    [MemberData(nameof(ExpectedIndexZero))]
    public void DeriveReceiveAddress_matches_network_prefix_and_validates(
        ChainId chain,
        string expectedPath,
        string expectedAddress)
    {
        _ = expectedPath;
        var derived = _sut.DeriveReceiveAddress(Mnemonic, chain, 0);
        Assert.Equal(expectedAddress, derived.Address);

        switch (chain)
        {
            case ChainId.Btc:
                Assert.StartsWith("bc1q", derived.Address, StringComparison.Ordinal);
                Assert.NotNull(BitcoinAddress.Create(derived.Address, Network.Main));
                break;
            case ChainId.Eth:
                Assert.StartsWith("0x", derived.Address, StringComparison.Ordinal);
                Assert.True(AddressUtil.Current.IsValidEthereumAddressHexFormat(derived.Address));
                Assert.True(AddressUtil.Current.IsChecksumAddress(derived.Address));
                break;
            case ChainId.Ltc:
                Assert.StartsWith("ltc1", derived.Address, StringComparison.Ordinal);
                Assert.NotNull(BitcoinAddress.Create(derived.Address, Litecoin.Instance.Mainnet));
                break;
            case ChainId.Doge:
                Assert.StartsWith("D", derived.Address, StringComparison.Ordinal);
                Assert.NotNull(BitcoinAddress.Create(derived.Address, Dogecoin.Instance.Mainnet));
                break;
            case ChainId.Tron:
                Assert.StartsWith("T", derived.Address, StringComparison.Ordinal);
                Assert.Equal(34, derived.Address.Length);
                break;
            case ChainId.Sol:
                // base58 ed25519 pubkey: 32–44 chars, no 0/O/I/l.
                Assert.InRange(derived.Address.Length, 32, 44);
                Assert.DoesNotContain(derived.Address, c => c is '0' or 'O' or 'I' or 'l');
                break;
            case ChainId.Ton:
            case ChainId.Ada:
            case ChainId.Xmr:
                throw new InvalidOperationException("Receive-only chains must not appear in supported vectors.");
            default:
                throw new ArgumentOutOfRangeException(nameof(chain), chain, null);
        }
    }

    [Fact]
    public void DeriveReceiveAddress_changes_with_index()
    {
        var zero = _sut.DeriveReceiveAddress(Mnemonic, ChainId.Btc, 0);
        var one = _sut.DeriveReceiveAddress(Mnemonic, ChainId.Btc, 1);

        Assert.NotEqual(zero.Address, one.Address);
        Assert.Equal("m/84'/0'/0'/0/1", one.DerivationPath);
    }

    [Fact]
    public void Ada_derives_a_supported_shelley_address()
    {
        var derived = _sut.DeriveReceiveAddress(Mnemonic, ChainId.Ada);

        Assert.StartsWith("addr1", derived.Address, StringComparison.Ordinal); // Shelley base address (bech32)
        Assert.Equal(103, derived.Address.Length);
        Assert.True(ChainCatalog.IsSupported(ChainId.Ada));  // now fully supported: receive, balance and send
        Assert.True(ChainCatalog.HasRealAddress(ChainId.Ada));
    }

    [Fact]
    public void Ton_derives_supported_v4r2_address()
    {
        var derived = _sut.DeriveReceiveAddress(Mnemonic, ChainId.Ton);

        Assert.StartsWith("UQ", derived.Address, StringComparison.Ordinal); // wallet v4R2 user-friendly form
        Assert.Equal(48, derived.Address.Length);
        Assert.True(ChainCatalog.IsSupported(ChainId.Ton)); // now fully supported: receive, balance and send
    }
}
