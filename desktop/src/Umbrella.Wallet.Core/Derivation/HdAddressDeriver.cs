using System.Security.Cryptography;
using NBitcoin;
using NBitcoin.Altcoins;
using NBitcoin.DataEncoders;
using Nethereum.Util;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Core.Derivation;

/// <summary>
/// Derives deterministic receive addresses from a BIP39 mnemonic for supported chains.
/// </summary>
public sealed class HdAddressDeriver
{
    private readonly Bip39MnemonicService _mnemonicService;

    public HdAddressDeriver(Bip39MnemonicService? mnemonicService = null)
    {
        _mnemonicService = mnemonicService ?? new Bip39MnemonicService();
    }

    /// <summary>
    /// The BIP39 passphrase applied when a caller doesn't pass one explicitly (i.e. passes null). Set
    /// once at unlock and shared by every service that derives through this instance (the scanner, the
    /// spender, the Bitcoin sender), so the whole app derives one wallet — there is no way to miss a
    /// call site and have the shown address disagree with the spent one. Empty = the normal wallet.
    /// Passing a non-null passphrase to a method overrides this (used by tests).
    /// </summary>
    public string ActivePassphrase { get; set; } = "";

    /// <summary>Resolves the effective passphrase: an explicit (non-null) argument wins, else the ambient.</summary>
    private string Resolve(string? passphrase) => passphrase ?? ActivePassphrase;

    /// <summary>
    /// Derives the external (receive) address at the given index for a supported chain.
    /// <paramref name="passphrase"/> is the optional BIP39 passphrase (the "25th word"): empty = the
    /// normal wallet; a non-empty value derives a wholly separate hidden wallet. It is mixed into the
    /// BIP39 seed, so it changes every chain — EXCEPT Cardano, whose Icarus scheme derives from the raw
    /// entropy and cannot honour a passphrase; asking for ADA with a passphrase throws rather than
    /// silently returning the base wallet's ADA address (which would defeat the hidden-wallet purpose).
    /// </summary>
    public ReceiveAddress DeriveReceiveAddress(string mnemonic, ChainId chain, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var info = ChainCatalog.Get(chain);
        if (!ChainCatalog.HasRealAddress(chain))
        {
            throw new UnsupportedChainException(chain);
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        var masterKey = parsed.DeriveExtKey(passphrase);

        return chain switch
        {
            ChainId.Btc => DeriveBitcoinLike(
                masterKey,
                ChainId.Btc,
                Network.Main,
                ScriptPubKeyType.Segwit,
                purpose: 84,
                coinType: 0,
                addressIndex),
            ChainId.Ltc => DeriveBitcoinLike(
                masterKey,
                ChainId.Ltc,
                Litecoin.Instance.Mainnet,
                ScriptPubKeyType.Segwit,
                purpose: 84,
                coinType: 2,
                addressIndex),
            ChainId.Doge => DeriveBitcoinLike(
                masterKey,
                ChainId.Doge,
                Dogecoin.Instance.Mainnet,
                ScriptPubKeyType.Legacy,
                purpose: 44,
                coinType: 3,
                addressIndex),
            ChainId.Bch => DeriveBitcoinLike(
                masterKey,
                ChainId.Bch,
                BCash.Instance.Mainnet,
                ScriptPubKeyType.Legacy,
                purpose: 44,
                coinType: 145,
                addressIndex),
            ChainId.Zec => DeriveZcashTransparent(masterKey, addressIndex),
            ChainId.Eth => DeriveEthereum(masterKey, addressIndex),
            ChainId.Tron => DeriveTron(masterKey, addressIndex),
            ChainId.Sol => DeriveSolana(parsed, addressIndex, passphrase),
            ChainId.Xmr => DeriveMonero(parsed, passphrase),
            ChainId.Ton => DeriveTon(parsed, passphrase),
            ChainId.Ada => HasPassphrase(passphrase)
                ? throw new PassphraseUnsupportedException(ChainId.Ada)
                : DeriveAda(parsed),
            _ => throw new ArgumentOutOfRangeException(nameof(chain), chain, "Unknown chain id."),
        };
    }

    /// <summary>True when a non-empty BIP39 passphrase is in play (a hidden wallet).</summary>
    private static bool HasPassphrase(string? passphrase) => !string.IsNullOrEmpty(passphrase);

    /// <summary>
    /// Solana: SLIP-0010 ed25519 at m/44'/501'/0'/{index}', base58 of the public key.
    /// Matches Phantom / solana-keygen for the account-0 address.
    /// </summary>
    private static ReceiveAddress DeriveSolana(Mnemonic parsed, uint addressIndex, string passphrase = "")
    {
        var seed = parsed.DeriveSeed(passphrase);
        var priv = Slip10Ed25519.DerivePrivateKey(seed, new[] { 44u, 501u, 0u, addressIndex });
        var pub = Slip10Ed25519.PublicKey(priv);
        var address = Encoders.Base58.EncodeData(pub);
        var path = $"44'/501'/0'/{addressIndex}'";
        return new ReceiveAddress(ChainId.Sol, address, "m/" + path, addressIndex);
    }

    /// <summary>
    /// TON: SLIP-0010 ed25519 at m/44'/607'/0', wallet v4R2 address (non-bounceable / UQ form).
    /// Matches multi-coin wallets (e.g. Trust Wallet) that use coin type 607 + v4R2, so the same
    /// BIP39 phrase recovers the funds there. The v4R2 address math is pinned to tonweb by a test.
    /// </summary>
    private static ReceiveAddress DeriveTon(Mnemonic parsed, string passphrase = "")
    {
        var seed = parsed.DeriveSeed(passphrase);
        var priv = Slip10Ed25519.DerivePrivateKey(seed, new[] { 44u, 607u, 0u });
        var pub = Slip10Ed25519.PublicKey(priv);
        var address = TonKeys.WalletV4R2Address(pub);
        return new ReceiveAddress(ChainId.Ton, address, "m/44'/607'/0'", 0);
    }

    /// <summary>
    /// Cardano: Icarus / CIP-1852 (BIP32-Ed25519) at m/1852'/1815'/0', Shelley base address.
    /// The whole pipeline is pinned to cardano-serialization-lib, so the same phrase recovers the
    /// funds in any CIP-1852 wallet.
    /// </summary>
    private static ReceiveAddress DeriveAda(Mnemonic parsed)
    {
        var address = AdaKeys.BaseAddress(parsed.ToString());
        return new ReceiveAddress(ChainId.Ada, address, "m/1852'/1815'/0'/0/0", 0);
    }

    private static ReceiveAddress DeriveBitcoinLike(
        ExtKey masterKey,
        ChainId chain,
        Network network,
        ScriptPubKeyType scriptType,
        int purpose,
        int coinType,
        uint addressIndex)
    {
        var path = new KeyPath($"{purpose}'/{coinType}'/0'/0/{addressIndex}");
        var derived = masterKey.Derive(path);
        var address = derived.PrivateKey.PubKey.GetAddress(scriptType, network).ToString();
        return new ReceiveAddress(chain, address, FormatPath(path), addressIndex);
    }

    /// <summary>
    /// Ethereum private key (32 bytes) at m/44'/60'/0'/0/{index}. Used transiently for local
    /// transaction signing only — the caller must zero the array after use.
    /// </summary>
    public byte[] DeriveEthereumPrivateKey(string mnemonic, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        var derived = parsed.DeriveExtKey(passphrase).Derive(new KeyPath($"44'/60'/0'/0/{addressIndex}"));
        return derived.PrivateKey.ToBytes();
    }

    /// <summary>
    /// The NBitcoin <see cref="Key"/> behind the displayed BTC/LTC receive address, for local
    /// signing only. Path matches <see cref="DeriveReceiveAddress"/> exactly (BIP84).
    /// </summary>
    public Key DeriveBitcoinLikeKey(string mnemonic, ChainId chain, uint addressIndex = 0, string? passphrase = null) =>
        DeriveBitcoinLikeAt(mnemonic, chain, change: 0, index: addressIndex, passphrase).PrivateKey;

    /// <summary>
    /// BIP84/44 parameters for the UTXO chains the wallet can build transactions for. Kept in one
    /// place so the address, the signing key and the change address can never drift apart.
    /// </summary>
    public static (int Purpose, int CoinType, NBitcoin.Network Network, ScriptPubKeyType ScriptType)
        BitcoinLikeParams(ChainId chain) => chain switch
    {
        ChainId.Btc => (84, 0, Network.Main, ScriptPubKeyType.Segwit),
        ChainId.Ltc => (84, 2, Litecoin.Instance.Mainnet, ScriptPubKeyType.Segwit),
        ChainId.Doge => (44, 3, Dogecoin.Instance.Mainnet, ScriptPubKeyType.Legacy),
        // Bitcoin Cash: BIP44 (m/44'/145'), P2PKH, CashAddr encoding. The BCash network also carries
        // the SIGHASH_FORKID rules NBitcoin needs to sign a spend correctly.
        ChainId.Bch => (44, 145, BCash.Instance.Mainnet, ScriptPubKeyType.Legacy),
        _ => throw new UnsupportedChainException(chain),
    };

    /// <summary>
    /// The full signing account (path + address + key + scriptPubKey) for a UTXO chain at an
    /// explicit (change, index) leaf. <paramref name="change"/> is the BIP44 change level:
    /// 0 = external (receive) chain, 1 = internal (change) chain. This is what the HD wallet uses to
    /// SEE and SPEND every address it has ever handed out — not just receive #0 — and to send change
    /// to a fresh internal address instead of re-using a public one. Address, key and scriptPubKey
    /// all come from this one method so they can never drift apart.
    /// </summary>
    public DerivedUtxoAccount DeriveBitcoinLikeAt(string mnemonic, ChainId chain, uint change, uint index, string? passphrase = null) =>
        DeriveUtxoAccount(mnemonic, new UtxoDerivationPath(chain, change, index), passphrase);

    /// <summary>Derives the signing account for an explicit <see cref="UtxoDerivationPath"/>.</summary>
    public DerivedUtxoAccount DeriveUtxoAccount(string mnemonic, UtxoDerivationPath path, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var (purpose, coinType, network, scriptType) = BitcoinLikeParams(path.Chain);
        var parsed = Bip39MnemonicService.ParseValidated(RequireNormalized(mnemonic));
        var keyPath = new KeyPath($"{purpose}'/{coinType}'/0'/{path.Change}/{path.Index}");
        var key = parsed.DeriveExtKey(passphrase).Derive(keyPath).PrivateKey;
        var address = key.PubKey.GetAddress(scriptType, network).ToString();
        var scriptPubKey = key.PubKey.GetAddress(scriptType, network).ScriptPubKey;
        return new DerivedUtxoAccount(path, address, key, scriptPubKey);
    }

    /// <summary>Validates a mnemonic and returns its normalized form, or throws with the reason.</summary>
    private string RequireNormalized(string mnemonic)
    {
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        return validation.NormalizedMnemonic;
    }

    /// <summary>
    /// TRON signing key at m/44'/195'/0'/0/{index} — same path as the displayed TRX address.
    /// Used for native TRX and USDT (TRC-20) transfers.
    /// </summary>
    public Key DeriveTronKey(string mnemonic, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return parsed.DeriveExtKey(passphrase)
            .Derive(new KeyPath($"44'/195'/0'/0/{addressIndex}"))
            .PrivateKey;
    }

    /// <summary>
    /// The full Monero account (address + secret keys) for this wallet. The secret keys are what
    /// "Restore from keys" consumes in Feather / monero-wallet-cli.
    /// </summary>
    public MoneroWallet DeriveMoneroWallet(string mnemonic, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return MoneroKeys.FromSeed(parsed.DeriveSeed(passphrase));
    }

    private static ReceiveAddress DeriveMonero(Mnemonic parsed, string passphrase = "")
    {
        var wallet = MoneroKeys.FromSeed(parsed.DeriveSeed(passphrase));
        return new ReceiveAddress(ChainId.Xmr, wallet.Address, "umbrella-monero-v1", 0);
    }

    /// <summary>
    /// Solana ed25519 secret scalar (32 bytes) at m/44'/501'/0'/{index}', for local signing only.
    /// </summary>
    public byte[] DeriveSolanaPrivateKey(string mnemonic, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return Slip10Ed25519.DerivePrivateKey(parsed.DeriveSeed(passphrase), new[] { 44u, 501u, 0u, addressIndex });
    }

    /// <summary>
    /// TON ed25519 secret scalar (32 bytes) at m/44'/607'/0', for signing v4R2 transfers locally.
    /// </summary>
    public byte[] DeriveTonPrivateKey(string mnemonic, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return Slip10Ed25519.DerivePrivateKey(parsed.DeriveSeed(passphrase), new[] { 44u, 607u, 0u });
    }

    private static ReceiveAddress DeriveEthereum(ExtKey masterKey, uint addressIndex)
    {
        var path = new KeyPath($"44'/60'/0'/0/{addressIndex}");
        var derived = masterKey.Derive(path);
        var addressBytes = GetSecp256k1AddressBytes(derived.PrivateKey.PubKey);
        var hex = "0x" + Encoders.Hex.EncodeData(addressBytes);
        var checksum = AddressUtil.Current.ConvertToChecksumAddress(hex);
        return new ReceiveAddress(ChainId.Eth, checksum, FormatPath(path), addressIndex);
    }

    /// <summary>
    /// Zcash transparent (t-addr) receive address at m/44'/133'/0'/0/{index}. A t-addr is an ordinary
    /// P2PKH — the SAME Hash160(compressed pubkey) as a Bitcoin address — differing only in Zcash's
    /// two-byte mainnet version prefix 0x1C 0xB8 (which renders as the "t1" leader), Base58Check with a
    /// double-SHA256 checksum. This is transparent-only: shielded (z-addr / unified) receiving is a
    /// separate scheme the wallet does not yet derive, so nothing here implies shielded support.
    /// </summary>
    private static ReceiveAddress DeriveZcashTransparent(ExtKey masterKey, uint addressIndex)
    {
        var path = new KeyPath($"44'/133'/0'/0/{addressIndex}");
        var derived = masterKey.Derive(path);
        var hash160 = derived.PrivateKey.PubKey.Hash.ToBytes(); // RIPEMD160(SHA256(compressed pubkey)), 20 bytes

        var payload = new byte[22];
        payload[0] = 0x1C;
        payload[1] = 0xB8;
        Buffer.BlockCopy(hash160, 0, payload, 2, 20);
        var address = EncodeBase58Check(payload);
        return new ReceiveAddress(ChainId.Zec, address, FormatPath(path), addressIndex);
    }

    private static ReceiveAddress DeriveTron(ExtKey masterKey, uint addressIndex)
    {
        var path = new KeyPath($"44'/195'/0'/0/{addressIndex}");
        var derived = masterKey.Derive(path);
        var addressBytes = GetSecp256k1AddressBytes(derived.PrivateKey.PubKey);

        // TRON mainnet: version byte 0x41 + 20-byte address, Base58Check.
        var payload = new byte[21];
        payload[0] = 0x41;
        Buffer.BlockCopy(addressBytes, 0, payload, 1, 20);
        var address = EncodeBase58Check(payload);
        return new ReceiveAddress(ChainId.Tron, address, FormatPath(path), addressIndex);
    }

    private static string FormatPath(KeyPath path) => "m/" + path;

    /// <summary>
    /// Keccak-256 of the uncompressed public key (without 0x04 prefix), last 20 bytes.
    /// Shared by Ethereum and TRON.
    /// </summary>
    private static byte[] GetSecp256k1AddressBytes(PubKey pubKey)
    {
        var uncompressed = pubKey.Decompress().ToBytes();
        if (uncompressed.Length != 65 || uncompressed[0] != 0x04)
        {
            throw new InvalidOperationException("Expected uncompressed secp256k1 public key.");
        }

        var hash = Sha3Keccack.Current.CalculateHash(uncompressed.AsSpan(1).ToArray());
        var address = new byte[20];
        Buffer.BlockCopy(hash, 12, address, 0, 20);
        return address;
    }

    private static string EncodeBase58Check(byte[] payload)
    {
        var checksum = DoubleSha256(payload);
        var data = new byte[payload.Length + 4];
        Buffer.BlockCopy(payload, 0, data, 0, payload.Length);
        Buffer.BlockCopy(checksum, 0, data, payload.Length, 4);
        return Encoders.Base58.EncodeData(data);
    }

    private static byte[] DoubleSha256(byte[] data)
    {
        var first = SHA256.HashData(data);
        return SHA256.HashData(first);
    }
}
