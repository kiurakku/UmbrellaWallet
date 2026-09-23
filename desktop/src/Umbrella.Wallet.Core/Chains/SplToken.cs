using System.Buffers.Binary;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>
/// SPL token transfers (roadmap N.3, send): the associated token account every wallet derives for a
/// (wallet, mint) pair, the TransferChecked instruction, and the idempotent "create the recipient's
/// token account" instruction.
///
/// TransferChecked, not Transfer: it names the mint and its decimals, so the token program itself
/// refuses a transfer whose amount was scaled for a different token. Only the original SPL Token
/// program is used; Token-2022 mints can carry transfer fees and hooks that change what a transfer
/// does, and are refused until each is handled.
/// </summary>
public static class SplToken
{
    public static readonly byte[] TokenProgram = Key(SolanaTokens.TokenProgram);
    public static readonly byte[] Token2022Program = Key(SolanaTokens.Token2022Program);
    public static readonly byte[] AssociatedTokenProgram = Key("ATokenGPvbdGVxr1b2hvZbsiqW5xWH25efTNsLJA8knL");
    public static readonly byte[] SystemProgram = new byte[32];

    /// <summary>The size of a token account, which sets the rent a new one must hold.</summary>
    public const int TokenAccountSize = 165;

    private const byte TransferCheckedTag = 12;
    private const byte CreateIdempotentTag = 1;

    /// <summary>The associated token account of a wallet for a mint under a token program: the one
    /// address every wallet, exchange and explorer expects that wallet's tokens of that mint to be in.
    /// The program is part of the derivation, so a Token-2022 mint has a different account.</summary>
    public static byte[] AssociatedTokenAddress(byte[] owner, byte[] mint, byte[]? tokenProgram = null) =>
        SolanaKeys.FindProgramAddress([owner, tokenProgram ?? TokenProgram, mint], AssociatedTokenProgram).Address;

    /// <summary>TransferChecked: [12, amount u64 LE, decimals]; source, mint, destination, owner (signer).</summary>
    public static SolanaInstruction TransferChecked(
        byte[] source, byte[] mint, byte[] destination, byte[] owner, ulong amount, byte decimals, byte[]? tokenProgram = null)
    {
        var data = new byte[10];
        data[0] = TransferCheckedTag;
        BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(1), amount);
        data[9] = decimals;
        return new SolanaInstruction(tokenProgram ?? TokenProgram,
        [
            new SolanaAccountMeta(source, false, true),
            new SolanaAccountMeta(mint, false, false),
            new SolanaAccountMeta(destination, false, true),
            new SolanaAccountMeta(owner, true, false),
        ], data);
    }

    /// <summary>Creates the wallet's associated token account for the mint if it does not exist, and
    /// does nothing if it does; the payer funds its rent.</summary>
    public static SolanaInstruction CreateAssociatedAccountIdempotent(byte[] payer, byte[] owner, byte[] mint, byte[]? tokenProgram = null)
    {
        var program = tokenProgram ?? TokenProgram;
        return new SolanaInstruction(
            AssociatedTokenProgram,
            [
                new SolanaAccountMeta(payer, true, true),
                new SolanaAccountMeta(AssociatedTokenAddress(owner, mint, program), false, true),
                new SolanaAccountMeta(owner, false, false),
                new SolanaAccountMeta(mint, false, false),
                new SolanaAccountMeta(SystemProgram, false, false),
                new SolanaAccountMeta(program, false, false),
            ],
            [CreateIdempotentTag]);
    }

    /// <summary>How big a token account of this program is, for its rent: 165 bytes under the original
    /// program, and more under Token-2022, where the account carries the mint's extensions. 200 covers
    /// what the extensions this wallet allows can add, so the check errs on the safe side.</summary>
    public static int AccountSizeFor(string? program) =>
        program == SolanaTokens.Token2022Program ? 200 : TokenAccountSize;

    /// <summary>
    /// Whether this wallet will send a mint of the Token-2022 program, given the extensions its account
    /// declares (as <c>getAccountInfo</c> jsonParsed lists them) — and if not, why not, in the user's
    /// terms. The original token program has no extensions and is always allowed.
    ///
    /// What is refused is anything that would make a plain TransferChecked move a different amount than
    /// the review promised, fail outright, or land somewhere frozen. What is allowed is everything that
    /// only concerns the issuer or metadata: a permanent delegate or freeze authority is the token's own
    /// nature, not something this transfer changes.
    /// </summary>
    public static string? WhyNotSendable(string? program, JsonElement mintInfo)
    {
        if (program != SolanaTokens.Token2022Program) return null;
        if (!mintInfo.TryGetProperty("extensions", out var extensions) || extensions.ValueKind != JsonValueKind.Array)
            return null;

        foreach (var extension in extensions.EnumerateArray())
        {
            var name = extension.TryGetProperty("extension", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            var state = extension.TryGetProperty("state", out var s) && s.ValueKind == JsonValueKind.Object ? s : default;
            switch (name)
            {
                case "nonTransferable" or "nonTransferableAccount":
                    return "this token cannot be transferred at all — the issuer made it non-transferable";
                case "transferFeeConfig" when HasTransferFee(state):
                    return "this token charges a transfer fee, which this wallet does not handle yet";
                case "transferHook" when state.TryGetProperty("programId", out var hook) && hook.ValueKind == JsonValueKind.String:
                    return "this token runs a transfer hook — another program has to approve the transfer — which this wallet does not handle yet";
                case "pausableConfig" when state.TryGetProperty("paused", out var paused) && paused.ValueKind == JsonValueKind.True:
                    return "the issuer has paused transfers of this token";
                case "defaultAccountState" when state.TryGetProperty("accountState", out var st) && st.GetString() != "initialized":
                    return "new accounts for this token start frozen, so the recipient could not use what you send";
                case "interestBearingConfig" or "scaledUiAmountConfig":
                    return "this token's displayed amount is scaled by the issuer, and this wallet would not be able to promise the amount that arrives";
            }
        }

        return null;
    }

    /// <summary>
    /// What the issuer of this token can still do to it once it has arrived — powers the recipient
    /// cannot refuse and no transfer changes. The runtime itself warns about a permanent delegate when
    /// an account is created; somebody sending the token deserves to read it first.
    /// </summary>
    public static string? IssuerPowers(JsonElement mintInfo)
    {
        var powers = new List<string>();
        if (mintInfo.TryGetProperty("freezeAuthority", out var freeze) && freeze.ValueKind == JsonValueKind.String)
            powers.Add("freeze it in any account");
        if (mintInfo.TryGetProperty("extensions", out var extensions) && extensions.ValueKind == JsonValueKind.Array &&
            extensions.EnumerateArray().Any(e =>
                e.TryGetProperty("extension", out var n) && n.GetString() == "permanentDelegate"))
            powers.Add("move it out of any account");

        return powers.Count == 0 ? null : $"The issuer of this token can {string.Join(" and ", powers)}, including the recipient's.";
    }

    /// <summary>A transfer fee that is actually charged, in either the current or the coming epoch.</summary>
    private static bool HasTransferFee(JsonElement state)
    {
        foreach (var which in new[] { "olderTransferFee", "newerTransferFee" })
        {
            if (!state.TryGetProperty(which, out var fee) || fee.ValueKind != JsonValueKind.Object) continue;
            var points = fee.TryGetProperty("transferFeeBasisPoints", out var b) && b.TryGetInt32(out var bp) ? bp : 0;
            var maximum = fee.TryGetProperty("maximumFee", out var m) && m.ValueKind == JsonValueKind.Number && m.TryGetUInt64(out var mx) ? mx : 0;
            if (points > 0 && maximum > 0) return true;
        }

        return false;
    }

    /// <summary>A token amount in the mint's smallest units: positive, and no more decimals than it has.</summary>
    public static bool TryToUnits(decimal amount, int decimals, out ulong units)
    {
        units = 0;
        if (amount <= 0 || decimals is < 0 or > 18) return false;
        var scaled = amount * Pow10(decimals);
        if (scaled != decimal.Truncate(scaled) || scaled > ulong.MaxValue) return false;
        units = (ulong)scaled;
        return true;
    }

    public static decimal FromUnits(ulong units, int decimals) => units / Pow10(decimals);

    /// <summary>What <c>getAccountInfo</c> (jsonParsed) says an account is.</summary>
    public enum AccountKind { Missing, Wallet, TokenAccount, Mint, Other, Unreadable }

    /// <summary>
    /// Reads a jsonParsed <c>getAccountInfo</c> result: nothing there, an ordinary System-owned wallet,
    /// a token account (with its mint and owner), a mint (with its program and decimals), or something
    /// else. The details are what a send has to check before it signs.
    /// </summary>
    public static (AccountKind Kind, string? Program, string? Mint, string? Owner, int Decimals, ulong Units) ParseAccount(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object || !result.TryGetProperty("value", out var value))
            return (AccountKind.Unreadable, null, null, null, 0, 0);
        if (value.ValueKind == JsonValueKind.Null) return (AccountKind.Missing, null, null, null, 0, 0);
        if (value.ValueKind != JsonValueKind.Object || !value.TryGetProperty("owner", out var ownerEl) || ownerEl.ValueKind != JsonValueKind.String)
            return (AccountKind.Unreadable, null, null, null, 0, 0);

        var program = ownerEl.GetString();
        if (program == "11111111111111111111111111111111") return (AccountKind.Wallet, program, null, null, 0, 0);
        if (program != SolanaTokens.TokenProgram && program != SolanaTokens.Token2022Program)
            return (AccountKind.Other, program, null, null, 0, 0);

        if (!value.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object ||
            !data.TryGetProperty("parsed", out var parsed) || parsed.ValueKind != JsonValueKind.Object ||
            !parsed.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String ||
            !parsed.TryGetProperty("info", out var info) || info.ValueKind != JsonValueKind.Object)
            return (AccountKind.Unreadable, program, null, null, 0, 0);

        switch (type.GetString())
        {
            case "mint" when info.TryGetProperty("decimals", out var d) && d.TryGetInt32(out var decimals):
                return (AccountKind.Mint, program, null, null, decimals, 0);
            case "account" when
                info.TryGetProperty("mint", out var m) && m.ValueKind == JsonValueKind.String &&
                info.TryGetProperty("owner", out var o) && o.ValueKind == JsonValueKind.String &&
                info.TryGetProperty("tokenAmount", out var ta) && ta.ValueKind == JsonValueKind.Object &&
                ta.TryGetProperty("amount", out var a) && ulong.TryParse(a.GetString(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var units) &&
                ta.TryGetProperty("decimals", out var td) && td.TryGetInt32(out var tokenDecimals):
                return (AccountKind.TokenAccount, program, m.GetString(), o.GetString(), tokenDecimals, units);
            default:
                return (AccountKind.Unreadable, program, null, null, 0, 0);
        }
    }

    private static decimal Pow10(int n)
    {
        var r = 1m;
        for (var i = 0; i < n; i++) r *= 10;
        return r;
    }

    private static byte[] Key(string base58) =>
        SolanaKeys.TryDecode(base58, out var key) ? key : throw new InvalidOperationException(base58);
}
