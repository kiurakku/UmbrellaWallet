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
            ChainId.Xrp => DeriveXrp(masterKey, addressIndex),
            ChainId.Xlm => DeriveStellar(parsed, addressIndex, passphrase),
            ChainId.Atom => DeriveCosmos(masterKey, addressIndex),
            ChainId.Near => DeriveNear(parsed, addressIndex, passphrase),
            ChainId.Dot => DerivePolkadot(parsed, passphrase),
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
    /// Stellar: SEP-0005 — SLIP-0010 ed25519 at m/44'/148'/{index}', every level hardened, encoded as
    /// a "G…" StrKey (roadmap N.5). Pinned to the SEP's published test vectors, which is what makes the
    /// phrase restore in LOBSTR, Solar or a Ledger.
    /// </summary>
    private static ReceiveAddress DeriveStellar(Mnemonic parsed, uint addressIndex, string passphrase = "")
    {
        var seed = parsed.DeriveSeed(passphrase);
        var priv = Slip10Ed25519.DerivePrivateKey(seed, new[] { 44u, 148u, addressIndex });
        var address = StellarKeys.EncodeAccountId(Slip10Ed25519.PublicKey(priv));
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
        return new ReceiveAddress(ChainId.Xlm, address, $"m/44'/148'/{addressIndex}'", addressIndex);
    }

    /// <summary>
    /// Polkadot root account: substrate-bip39 mini secret from the phrase's ENTROPY (+ passphrase), an
    /// sr25519 key, SS58 with the Polkadot prefix (roadmap N.8). Pinned to subkey's documented output.
    /// </summary>
    private static ReceiveAddress DerivePolkadot(Mnemonic parsed, string passphrase = "")
    {
        var entropy = AdaKeys.EntropyFromMnemonic(parsed.ToString());
        var mini = Umbrella.Wallet.Core.Polkadot.PolkadotKeys.MiniSecretFromEntropy(entropy, passphrase);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(entropy);
        var publicKey = Umbrella.Wallet.Core.Polkadot.PolkadotKeys.PublicKeyFromMiniSecret(mini);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(mini);
        return new ReceiveAddress(ChainId.Dot, Umbrella.Wallet.Core.Polkadot.Ss58.Encode(publicKey), "sr25519 root", 0);
    }

    /// <summary>
    /// The sr25519 key the Polkadot address comes from (roadmap N.8, send): the same mini secret, expanded
    /// the way schnorrkel does. The caller disposes it.
    /// </summary>
    public Umbrella.Wallet.Core.Polkadot.Sr25519.Keypair DeriveDotKeypair(string mnemonic, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var entropy = AdaKeys.EntropyFromMnemonic(validation.NormalizedMnemonic);
        var mini = Umbrella.Wallet.Core.Polkadot.PolkadotKeys.MiniSecretFromEntropy(entropy, passphrase);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(entropy);
        try
        {
            return Umbrella.Wallet.Core.Polkadot.Sr25519.FromMiniSecret(mini);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(mini);
        }
    }

    /// <summary>
    /// NEAR implicit account at m/44'/397'/{index}' (SLIP-0010 ed25519): the account id is the hex of
    /// the public key (roadmap N.7). Pinned to near-seed-phrase's own parse test.
    /// </summary>
    private static ReceiveAddress DeriveNear(Mnemonic parsed, uint addressIndex, string passphrase = "")
    {
        var pub = DeriveNearPublicKey(parsed, addressIndex, passphrase);
        return new ReceiveAddress(ChainId.Near, NearAccounts.ImplicitAccountId(pub), $"m/44'/397'/{addressIndex}'", addressIndex);
    }

    private static byte[] DeriveNearPublicKey(Mnemonic parsed, uint addressIndex, string passphrase)
    {
        var priv = Slip10Ed25519.DerivePrivateKey(parsed.DeriveSeed(passphrase), new[] { 44u, 397u, addressIndex });
        var pub = Slip10Ed25519.PublicKey(priv);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(priv);
        return pub;
    }

    /// <summary>The NEAR ed25519 public key, for tests and for a future signer.</summary>
    public byte[] DeriveNearPublicKey(string mnemonic, uint addressIndex = 0, string? passphrase = null) =>
        DeriveNearPublicKey(Bip39MnemonicService.ParseValidated(RequireNormalized(mnemonic)), addressIndex, Resolve(passphrase));

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
    public DerivedUtxoAccount DeriveBitcoinLikeAt(
        string mnemonic, ChainId chain, uint change, uint index, string? passphrase = null,
        UtxoScriptKind kind = UtxoScriptKind.Default) =>
        DeriveUtxoAccount(mnemonic, new UtxoDerivationPath(chain, change, index, kind), passphrase);

    /// <summary>Derives the signing account for an explicit <see cref="UtxoDerivationPath"/>.</summary>
    public DerivedUtxoAccount DeriveUtxoAccount(string mnemonic, UtxoDerivationPath path, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var (_, _, network, _) = BitcoinLikeParams(path.Chain);
        var (_, scriptType) = BranchParams(path.Chain, path.Kind);

        var parsed = Bip39MnemonicService.ParseValidated(RequireNormalized(mnemonic));
        var keyPath = KeyPathFor(path);
        var key = parsed.DeriveExtKey(passphrase).Derive(keyPath).PrivateKey;
        var address = key.PubKey.GetAddress(scriptType, network).ToString();
        var scriptPubKey = key.PubKey.GetAddress(scriptType, network).ScriptPubKey;
        return new DerivedUtxoAccount(path, address, key, scriptPubKey);
    }

    /// <summary>BIP86: the purpose Taproot accounts live at.</summary>
    public const int TaprootPurpose = 86;

    /// <summary>The full BIP32 path of a UTXO leaf, <c>purpose'/coin'/0'/change/index</c>. The one
    /// place it is spelled, so the key that signs and the path a PSBT names cannot disagree.</summary>
    public static KeyPath KeyPathFor(UtxoDerivationPath path)
    {
        var (_, coinType, _, _) = BitcoinLikeParams(path.Chain);
        var (purpose, _) = BranchParams(path.Chain, path.Kind);
        return new KeyPath($"{purpose}'/{coinType}'/0'/{path.Change}/{path.Index}");
    }

    /// <summary>
    /// Purpose and script type for one branch of a UTXO chain. Taproot is a different purpose AND a
    /// different script type on the same coin; both move together or the address and the key belong
    /// to different wallets (roadmap P2.1). Bitcoin only — any other chain throws rather than invent
    /// an address nothing scans.
    /// </summary>
    public static (int Purpose, ScriptPubKeyType ScriptType) BranchParams(ChainId chain, UtxoScriptKind kind)
    {
        var (purpose, _, _, scriptType) = BitcoinLikeParams(chain);
        if (kind != UtxoScriptKind.Taproot) return (purpose, scriptType);
        if (chain != ChainId.Btc) throw new UnsupportedChainException(chain);
        return (TaprootPurpose, ScriptPubKeyType.TaprootBIP86);
    }

    /// <summary>
    /// The account-level extended public key of one branch (<c>m/purpose'/coin'/0'</c>). Derived
    /// once, its children give every address on that branch without re-running the BIP39 seed
    /// stretch per address — which is what makes recognising "is this output ours?" across a few
    /// hundred addresses cheap (roadmap H.1).
    /// </summary>
    public ExtPubKey DeriveAccountExtPubKey(
        string mnemonic, ChainId chain, UtxoScriptKind kind = UtxoScriptKind.Default, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var (_, coinType, _, _) = BitcoinLikeParams(chain);
        var (purpose, _) = BranchParams(chain, kind);
        var parsed = Bip39MnemonicService.ParseValidated(RequireNormalized(mnemonic));
        return parsed.DeriveExtKey(passphrase).Derive(new KeyPath($"{purpose}'/{coinType}'/0'")).Neuter();
    }

    /// <summary>The BIP32 fingerprint of the wallet's master key — what a PSBT names so another tool
    /// (Sparrow, a hardware wallet) can recognise which of its inputs it holds keys for. It is a
    /// 4-byte hash, not a key; it identifies the wallet to whoever sees the PSBT.</summary>
    public HDFingerprint MasterFingerprint(string mnemonic, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var parsed = Bip39MnemonicService.ParseValidated(RequireNormalized(mnemonic));
        return parsed.DeriveExtKey(passphrase).Neuter().PubKey.GetHDFingerPrint();
    }

    /// <summary>
    /// The account-level EXTENDED PUBLIC KEY for a UTXO chain — everything a third party needs to
    /// list this wallet's addresses and its balance, and nothing that can spend a satoshi
    /// (roadmap P1.20).
    ///
    /// This is what "verify, don't trust" needs to mean something. A user who has to take the
    /// wallet's word for their balance is trusting the program that also tells them it is safe; an
    /// xpub lets them ask an independent scanner the same question and compare answers.
    ///
    /// It is also the most privacy-revealing thing the wallet can export: it discloses EVERY address
    /// on the account, past and future, to whoever receives it. The UI says so before showing it.
    /// </summary>
    public string DeriveAccountXpub(
        string mnemonic, ChainId chain, string? passphrase = null, UtxoScriptKind kind = UtxoScriptKind.Default)
    {
        var (_, _, network, _) = BitcoinLikeParams(chain);

        // The ACCOUNT level (m/purpose'/coin'/0'), exactly the level the addresses hang off — so what
        // a scanner derives from it is the same set the wallet scans and spends from.
        return DeriveAccountExtPubKey(mnemonic, chain, kind, passphrase).ToString(network);
    }

    /// <summary>The BIP32 path that <see cref="DeriveAccountXpub"/> exports, for the UI to show
    /// beside it — a key without its path is a key somebody has to guess at.</summary>
    public static string AccountXpubPath(ChainId chain, UtxoScriptKind kind = UtxoScriptKind.Default)
    {
        var (_, coinType, _, _) = BitcoinLikeParams(chain);
        var (purpose, _) = BranchParams(chain, kind);
        return $"m/{purpose}'/{coinType}'/0'";
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
    /// Stellar ed25519 seed (32 bytes) at m/44'/148'/0' — the SEP-0005 path <see cref="DeriveStellar"/>
    /// shows the address for, so the key signs for exactly the account on screen. Local signing only.
    /// </summary>
    public byte[] DeriveStellarPrivateKey(string mnemonic, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return Slip10Ed25519.DerivePrivateKey(parsed.DeriveSeed(passphrase), new[] { 44u, 148u, 0u });
    }

    /// <summary>
    /// NEAR ed25519 seed (32 bytes) at m/44'/397'/0' — the near-seed-phrase path <see cref="DeriveNear"/>
    /// shows the implicit account for, so the key signs for exactly the account on screen.
    /// </summary>
    public byte[] DeriveNearPrivateKey(string mnemonic, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return Slip10Ed25519.DerivePrivateKey(parsed.DeriveSeed(passphrase), new[] { 44u, 397u, 0u });
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

    /// <summary>
    /// XRP Ledger classic address at m/44'/144'/0'/0/{index}: the account id is
    /// RIPEMD160(SHA256(compressed secp256k1 public key)), encoded with XRPL's base58 (roadmap N.4).
    /// Pinned end to end: the public key against xrpl.js's own <c>fromMnemonic</c> test, the
    /// encoding against the worked example and sentinel accounts in XRPL's documentation.
    /// </summary>
    private static ReceiveAddress DeriveXrp(ExtKey masterKey, uint addressIndex)
    {
        var path = new KeyPath($"44'/144'/0'/0/{addressIndex}");
        var pubKey = masterKey.Derive(path).PrivateKey.PubKey;   // compressed, 33 bytes
        var accountId = XrpAddress.AccountIdFromPublicKey(pubKey.ToBytes());
        return new ReceiveAddress(ChainId.Xrp, XrpAddress.Encode(accountId), FormatPath(path), addressIndex);
    }

    /// <summary>
    /// Cosmos Hub address at m/44'/118'/0'/0/{index}: bech32("cosmos", RIPEMD160(SHA256(compressed
    /// key))) (roadmap N.6). Pinned to cosmjs's DirectSecp256k1HdWallet test — key and address.
    /// </summary>
    private static ReceiveAddress DeriveCosmos(ExtKey masterKey, uint addressIndex)
    {
        var path = new KeyPath($"44'/118'/0'/0/{addressIndex}");
        var accountId = masterKey.Derive(path).PrivateKey.PubKey.Hash.ToBytes();   // RIPEMD160(SHA256(pubkey))
        return new ReceiveAddress(ChainId.Atom, CosmosHub.AddressFromAccountId(accountId), FormatPath(path), addressIndex);
    }

    /// <summary>
    /// Cosmos Hub signing key at m/44'/118'/0'/0/{index} — the key the displayed ATOM address comes from
    /// (roadmap N.6, send). The caller disposes it.
    /// </summary>
    public Key DeriveCosmosKey(string mnemonic, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return parsed.DeriveExtKey(passphrase).Derive(new KeyPath($"44'/118'/0'/0/{addressIndex}")).PrivateKey;
    }

    /// <summary>The compressed public key behind the Cosmos address, for tests and for the signer's checks.</summary>
    public PubKey DeriveCosmosPublicKey(string mnemonic, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var parsed = Bip39MnemonicService.ParseValidated(RequireNormalized(mnemonic));
        return parsed.DeriveExtKey(passphrase).Derive(new KeyPath($"44'/118'/0'/0/{addressIndex}")).PrivateKey.PubKey;
    }

    /// <summary>
    /// XRP signing key at m/44'/144'/0'/0/{index} — the key the displayed XRP address comes from
    /// (roadmap N.4, send). The caller disposes it.
    /// </summary>
    public Key DeriveXrpKey(string mnemonic, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return parsed.DeriveExtKey(passphrase).Derive(new KeyPath($"44'/144'/0'/0/{addressIndex}")).PrivateKey;
    }

    /// <summary>The compressed public key behind the XRP address, for tests and for the signer's checks.</summary>
    public PubKey DeriveXrpPublicKey(string mnemonic, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var parsed = Bip39MnemonicService.ParseValidated(RequireNormalized(mnemonic));
        return parsed.DeriveExtKey(passphrase).Derive(new KeyPath($"44'/144'/0'/0/{addressIndex}")).PrivateKey.PubKey;
    }

    /// <summary>
    /// Zcash transparent signing key at m/44'/133'/0'/0/{index} — the key behind the displayed t1…
    /// address. The path is written out here rather than routed through the BIP84 UTXO machinery,
    /// because Zcash is BIP44/P2PKH and a shared helper that drifted would sign for an address the
    /// user was never shown. The caller disposes it.
    /// </summary>
    public Key DeriveZcashKey(string mnemonic, uint addressIndex = 0, string? passphrase = null)
    {
        passphrase = Resolve(passphrase);
        var validation = _mnemonicService.Validate(mnemonic);
        if (!validation.IsValid || validation.NormalizedMnemonic is null)
        {
            throw new ArgumentException(validation.Error ?? "Invalid mnemonic.", nameof(mnemonic));
        }

        var parsed = Bip39MnemonicService.ParseValidated(validation.NormalizedMnemonic);
        return parsed.DeriveExtKey(passphrase).Derive(new KeyPath($"44'/133'/0'/0/{addressIndex}")).PrivateKey;
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
