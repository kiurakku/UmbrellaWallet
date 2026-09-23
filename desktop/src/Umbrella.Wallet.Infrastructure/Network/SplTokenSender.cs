using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Infrastructure.Network;

/// <summary>An SPL token transfer as reviewed: everything <see cref="SplTokenSender.SignAndBroadcastAsync"/> will sign.</summary>
public sealed record SplSendQuote(
    string From,
    string To,
    string Mint,
    string Symbol,
    decimal Amount,
    ulong Units,
    byte Decimals,
    bool CreatesAccount,
    ulong RentLamports,
    string Server,
    string TokenProgram = SolanaTokens.TokenProgram,
    /// <summary>What the issuer can still do to this token after it arrives, when that is anything.</summary>
    string? IssuerPowers = null)
{
    public decimal FeeSol => SolanaRpc.ToSol(SolanaRpc.BaseFeeLamports);
    public decimal RentSol => SolanaRpc.ToSol(RentLamports);
}

/// <summary>
/// Real SPL token transfers (roadmap N.3, send). Everything the transfer depends on is read from the
/// chain, not from the holdings list: the mint's program and decimals, the balance in the sender's
/// associated token account, what the destination is, and whether its token account exists yet. The
/// transfer is TransferChecked — the token program re-checks the mint and decimals itself — preceded,
/// only when the recipient has no account for this token, by an idempotent create that the sender pays
/// rent for, which the review states.
///
/// Both token programs are sent: the original, and Token-2022 for mints whose extensions cannot change
/// what a plain transfer does (<see cref="SplToken.WhyNotSendable"/>). One that could — a transfer fee,
/// a transfer hook, a paused or non-transferable token, accounts that start frozen, or a scaled display
/// amount — is refused in the user's own terms rather than sent and hoped for.
/// </summary>
public sealed class SplTokenSender
{
    public async Task<(SplSendQuote? Quote, string? Error)> PrepareAsync(
        string from, string mint, string symbol, string to, decimal amount, CancellationToken ct = default)
    {
        to = to.Trim();
        if (!SolanaKeys.TryDecode(to, out var toKey)) return (null, "That is not a valid Solana address.");
        if (!SolanaKeys.TryDecode(from, out var fromKey) || !SolanaKeys.TryDecode(mint, out var mintKey))
            return (null, "This token's details are not valid. Nothing was sent.");
        if (string.Equals(to, from, StringComparison.Ordinal)) return (null, "That is this wallet's own Solana address.");

        // A wallet address is a point on the curve. A program-derived address is not — tokens sent to
        // one are held by whatever program owns it, which for a mistyped or copied address is nobody.
        if (!SolanaKeys.IsOnCurve(toKey))
            return (null, "That address belongs to a program, not a wallet. Ask for the recipient's wallet address.");

        foreach (var server in SolanaNetwork.Servers())
        {
            var host = new Uri(server).Host;

            var (mintInfo, _) = await SolanaNetwork.CallAsync(server, Info(mint), ct);
            if (mintInfo is null) continue;   // this server did not answer; try the next
            var m = SplToken.ParseAccount(mintInfo.Value);
            if (m.Kind != SplToken.AccountKind.Mint)
                return (null, $"{host} does not show {Short(mint)} as a token mint. Nothing was sent.");
            if (m.Program is not (SolanaTokens.TokenProgram or SolanaTokens.Token2022Program))
                return (null, $"{Short(mint)} is not held by a token program this wallet knows. Nothing was sent.");
            if (SplToken.WhyNotSendable(m.Program, MintInfo(mintInfo.Value)) is { } why)
                return (null, $"{symbol} cannot be sent from here: {why}. Nothing was sent.");
            if (!SolanaKeys.TryDecode(m.Program, out var tokenProgram))
                return (null, "This token's program could not be read. Nothing was sent.");
            if (!SplToken.TryToUnits(amount, m.Decimals, out var units))
                return (null, $"Enter a positive amount with at most {m.Decimals} decimal places.");

            // The sender's tokens: in its associated account, as every wallet keeps them.
            var sourceAta = SolanaKeys.Encode(SplToken.AssociatedTokenAddress(fromKey, mintKey, tokenProgram));
            var (sourceInfo, _) = await SolanaNetwork.CallAsync(server, Info(sourceAta), ct);
            var source = Parse(sourceInfo);
            if (source.Kind != SplToken.AccountKind.TokenAccount || source.Mint != mint || source.Owner != from)
                return (null, $"Could not find this wallet's {symbol} account on {host}. Nothing was sent.");
            if (source.Units < units)
            {
                return (null,
                    $"Not enough {symbol}: this wallet holds {SplToken.FromUnits(source.Units, m.Decimals)} {symbol}.");
            }

            // What the destination is: a wallet (funded or not yet) — never a token account, which
            // would be treated as a wallet and have a second token account made for it.
            var (destInfo, _) = await SolanaNetwork.CallAsync(server, Info(to), ct);
            var dest = Parse(destInfo);
            switch (dest.Kind)
            {
                case SplToken.AccountKind.TokenAccount:
                    return (null, "That is a token account, not a wallet address. Ask for the recipient's wallet (owner) address.");
                case SplToken.AccountKind.Mint or SplToken.AccountKind.Other:
                    return (null, "That address is not a wallet. Ask for the recipient's wallet address.");
                case SplToken.AccountKind.Unreadable:
                    return (null, $"Could not check the destination on {host}. Nothing was sent.");
            }

            var destAta = SolanaKeys.Encode(SplToken.AssociatedTokenAddress(toKey, mintKey, tokenProgram));
            var (destAtaInfo, _) = await SolanaNetwork.CallAsync(server, Info(destAta), ct);
            var destAccount = Parse(destAtaInfo);
            if (destAccount.Kind is not (SplToken.AccountKind.Missing or SplToken.AccountKind.TokenAccount) ||
                (destAccount.Kind == SplToken.AccountKind.TokenAccount && (destAccount.Mint != mint || destAccount.Owner != to)))
                return (null, $"Could not check the recipient's {symbol} account on {host}. Nothing was sent.");
            var creates = destAccount.Kind == SplToken.AccountKind.Missing;

            ulong rent = 0;
            if (creates)
            {
                var r = await SolanaNetwork.LamportsAsync(server,
                    SolanaRpc.Request("getMinimumBalanceForRentExemption", SplToken.AccountSizeFor(m.Program)), ct);
                if (r is null) return (null, $"Could not read the rent for a token account from {host}. Nothing was sent.");
                rent = r.Value;
            }

            // The fee, and the new account's rent, come out of SOL.
            var sol = await SolanaNetwork.LamportsAsync(server, SolanaRpc.Request("getBalance", from, new { commitment = "confirmed" }), ct);
            if (sol is null) return (null, $"Could not read the SOL balance from {host}. Nothing was sent.");
            var needed = SolanaRpc.BaseFeeLamports + rent;
            if (sol < needed)
            {
                return (null, creates
                    ? $"Not enough SOL for this transfer: it needs {SolanaRpc.Sol(needed)} SOL — the fee and the rent for the recipient's new {symbol} account."
                    : $"Not enough SOL for the fee: it needs {SolanaRpc.Sol(needed)} SOL.");
            }

            return (new SplSendQuote(from, to, mint, symbol, amount, units, (byte)m.Decimals, creates, rent, server, m.Program!,
                SplToken.IssuerPowers(MintInfo(mintInfo.Value))), null);
        }

        return (null, "No Solana server answered. Check your connection (or Tor). Nothing was sent.");
    }

    public async Task<SolSendOutcome> SignAndBroadcastAsync(SplSendQuote quote, byte[] privateKey, CancellationToken ct = default)
    {
        if (!SolanaKeys.TryDecode(quote.From, out var from) || !SolanaKeys.TryDecode(quote.To, out var to) ||
            !SolanaKeys.TryDecode(quote.Mint, out var mint))
            return new SolSendOutcome(SolSubmitOutcome.Rejected, null, "Invalid address. Nothing was sent.");

        var publicKey = new byte[32];
        Org.BouncyCastle.Math.EC.Rfc8032.Ed25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
        if (!publicKey.AsSpan().SequenceEqual(from))
            return new SolSendOutcome(SolSubmitOutcome.Rejected, null, "Key does not match the sending address — refusing to sign.");

        return await SolanaNetwork.SignSubmitAndFollowAsync(quote.Server, from, Instructions(quote, from, to, mint), privateKey, ct);
    }

    /// <summary>The instructions a quote signs: the recipient's account first when it is missing, then
    /// the TransferChecked from the sender's account to it.</summary>
    public static IReadOnlyList<SolanaInstruction> Instructions(SplSendQuote quote, byte[] from, byte[] to, byte[] mint)
    {
        if (!SolanaKeys.TryDecode(quote.TokenProgram, out var program))
            throw new InvalidOperationException("The token's program is not an address.");

        var list = new List<SolanaInstruction>();
        if (quote.CreatesAccount) list.Add(SplToken.CreateAssociatedAccountIdempotent(from, to, mint, program));
        list.Add(SplToken.TransferChecked(
            SplToken.AssociatedTokenAddress(from, mint, program), mint, SplToken.AssociatedTokenAddress(to, mint, program), from,
            quote.Units, quote.Decimals, program));
        return list;
    }

    /// <summary>The <c>info</c> object of a jsonParsed account — where a mint's extensions are listed.</summary>
    private static System.Text.Json.JsonElement MintInfo(System.Text.Json.JsonElement result) =>
        result.TryGetProperty("value", out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Object &&
        value.TryGetProperty("data", out var data) && data.TryGetProperty("parsed", out var parsed) &&
        parsed.TryGetProperty("info", out var info)
            ? info
            : default;

    /// <summary>A lookup that got no answer is unreadable — never "missing", which would read as a
    /// new, empty account and let a send go ahead on a guess.</summary>
    private static (SplToken.AccountKind Kind, string? Program, string? Mint, string? Owner, int Decimals, ulong Units) Parse(
        System.Text.Json.JsonElement? result) =>
        result is { } r ? SplToken.ParseAccount(r) : (SplToken.AccountKind.Unreadable, null, null, null, 0, 0);

    private static object Info(string address) =>
        SolanaRpc.Request("getAccountInfo", address, new { encoding = "jsonParsed", commitment = "confirmed" });

    private static string Short(string s) => s.Length > 10 ? $"{s[..4]}…{s[^4..]}" : s;
}
