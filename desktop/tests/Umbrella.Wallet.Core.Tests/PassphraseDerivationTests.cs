using NBitcoin;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Derivation;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// BIP39 passphrase ("25th word") derivation — the hidden-wallet feature. These pin the ONE new
/// behaviour (mixing a passphrase into the seed) to canonical vectors and prove the invariants that
/// keep funds safe: empty passphrase is unchanged, a passphrase yields a different deterministic
/// wallet, the shown address equals the signing key, and Cardano refuses a passphrase rather than
/// leaking the base wallet's address.
/// </summary>
public class PassphraseDerivationTests
{
    private readonly HdAddressDeriver _sut = new();
    private const string Mnemonic = Bip39MnemonicServiceTests.FixedTwentyFourWordMnemonic;

    /// <summary>Independent anchor: the first published BIP39 test vector (Trezor). If NBitcoin's
    /// passphrase→seed ever drifted from BIP39, this fails — so our passphrase is provably standard.</summary>
    [Fact]
    public void Bip39_passphrase_seed_matches_canonical_trezor_vector()
    {
        var m = new Mnemonic(
            "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about");
        var seedHex = NBitcoin.DataEncoders.Encoders.Hex.EncodeData(m.DeriveSeed("TREZOR"));
        Assert.Equal(
            "c55257c360c07c72029aebc1b53c05ed0362ada38ead3e3e9efa3708e53495531f09a6987599d18264c1e1c92f2cf141630c7a3c4ab7c81b2f001698e7463b04",
            seedHex);
    }

    /// <summary>An empty passphrase must be byte-for-byte the old behaviour, so every existing wallet
    /// keeps the exact same addresses (zero regression).</summary>
    [Theory]
    [InlineData(ChainId.Btc)]
    [InlineData(ChainId.Eth)]
    [InlineData(ChainId.Ltc)]
    [InlineData(ChainId.Tron)]
    [InlineData(ChainId.Sol)]
    [InlineData(ChainId.Ton)]
    [InlineData(ChainId.Xmr)]
    public void Empty_passphrase_equals_no_passphrase(ChainId chain)
    {
        var withEmpty = _sut.DeriveReceiveAddress(Mnemonic, chain, 0, "").Address;
        var without = _sut.DeriveReceiveAddress(Mnemonic, chain, 0).Address;
        Assert.Equal(without, withEmpty);
    }

    /// <summary>A passphrase produces a wholly different — but deterministic — wallet on every
    /// seed-based chain.</summary>
    [Theory]
    [InlineData(ChainId.Btc)]
    [InlineData(ChainId.Eth)]
    [InlineData(ChainId.Ltc)]
    [InlineData(ChainId.Tron)]
    [InlineData(ChainId.Sol)]
    [InlineData(ChainId.Ton)]
    [InlineData(ChainId.Xmr)]
    public void Passphrase_yields_a_different_deterministic_address(ChainId chain)
    {
        var baseAddr = _sut.DeriveReceiveAddress(Mnemonic, chain, 0, "").Address;
        var hidden1 = _sut.DeriveReceiveAddress(Mnemonic, chain, 0, "hunter2").Address;
        var hidden2 = _sut.DeriveReceiveAddress(Mnemonic, chain, 0, "hunter2").Address;
        Assert.NotEqual(baseAddr, hidden1);
        Assert.Equal(hidden1, hidden2);
    }

    /// <summary>Our deriver must thread the passphrase into the exact same BIP84 path NBitcoin derives
    /// directly — catches any bug in how we pass the passphrase through.</summary>
    [Fact]
    public void Btc_passphrase_address_matches_direct_nbitcoin()
    {
        const string pass = "correct horse battery staple";
        var expected = new Mnemonic(Mnemonic)
            .DeriveExtKey(pass)
            .Derive(new KeyPath("84'/0'/0'/0/0"))
            .PrivateKey.PubKey.GetAddress(ScriptPubKeyType.Segwit, Network.Main).ToString();
        var actual = _sut.DeriveReceiveAddress(Mnemonic, ChainId.Btc, 0, pass).Address;
        Assert.Equal(expected, actual);
    }

    /// <summary>THE safety invariant: under a passphrase, the address shown to the user is exactly the
    /// address of the key that will sign — so a hidden wallet can always spend what it displays.</summary>
    [Fact]
    public void Passphrase_shown_address_equals_signing_key_address()
    {
        const string pass = "s3cr3t";
        var shown = _sut.DeriveReceiveAddress(Mnemonic, ChainId.Btc, 0, pass).Address;
        var signing = _sut.DeriveBitcoinLikeAt(Mnemonic, ChainId.Btc, change: 0, index: 0, passphrase: pass);
        Assert.Equal(shown, signing.Address);
    }

    /// <summary>Cardano's Icarus scheme ignores the passphrase, so deriving ADA with one must refuse
    /// (rather than silently return the base wallet's ADA, which would defeat the hidden wallet). The
    /// normal no-passphrase ADA path still works.</summary>
    [Fact]
    public void Ada_refuses_a_passphrase_but_works_without_one()
    {
        Assert.Throws<PassphraseUnsupportedException>(
            () => _sut.DeriveReceiveAddress(Mnemonic, ChainId.Ada, 0, "hunter2"));

        var ada = _sut.DeriveReceiveAddress(Mnemonic, ChainId.Ada, 0, "");
        Assert.StartsWith("addr1", ada.Address);
    }

    /// <summary>
    /// Roadmap P1.2 — the hidden wallet has to be reachable.
    ///
    /// Everything above proves the derivation. None of it helped: the view model held
    /// <c>UnlockPassphrase</c>, the deriver, scanner and spender all honoured it, and the unlock
    /// screen had no field to type it into, so the feature existed and could not be used by anybody.
    /// A capability with no way in is the same as not having it, and it is invisible in every test
    /// that only exercises the layer below the window.
    /// </summary>
    [Fact]
    public void The_unlock_screen_offers_the_passphrase_field()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "desktop", "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var xaml = File.ReadAllText(Path.Combine(
            dir!.FullName, "desktop", "src", "Umbrella.Wallet.App", "Views", "MainWindow.axaml"));

        Assert.Contains("{Binding UnlockPassphrase}", xaml, StringComparison.Ordinal);

        // And it stays folded away, so the unlock screen does not advertise that hidden wallets are
        // a thing this wallet does to whoever is standing behind the user.
        Assert.Contains("ShowUnlockAdvanced", xaml, StringComparison.Ordinal);
    }
}
