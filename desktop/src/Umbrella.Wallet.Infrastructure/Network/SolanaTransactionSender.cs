using System.Net.Http.Json;
using System.Text.Json;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Umbrella.Wallet.Core.Chains;
using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record SolSendQuote(
    string From, string To, decimal AmountSol, ulong Lamports, decimal FeeSol,
    string? DevFeeTo = null, ulong DevFeeLamports = 0, string Server = SolanaNetwork.DefaultRpc);

/// <summary>How a Solana send ended, in the terms the Send screen needs.</summary>
public sealed record SolSendOutcome(SolSubmitOutcome Outcome, string? Signature, string? Message);

/// <summary>
/// Real Solana transfers. Builds the legacy message with <see cref="SolanaMessage"/> (System Program
/// transfer, plus the developer fee when there is one), signs it with ed25519, submits it once and
/// follows THAT signature until a confirmed block holds it or its blockhash expires.
///
/// The transaction id is the signature, known before anything is sent — so an answer that never
/// comes is followed up, never offered as a retry that could pay twice.
/// </summary>
public sealed class SolanaTransactionSender
{
    private static readonly byte[] SystemProgramId = new byte[32]; // all-zero = System Program

    public async Task<(SolSendQuote? Quote, string? Error)> PrepareAsync(
        string from, string to, decimal amountSol,
        string? devFeeAddress = null, decimal devFeeAmount = 0m, CancellationToken ct = default)
    {
        to = to.Trim();
        if (!SolanaKeys.TryDecode(to, out _)) return (null, "That is not a valid Solana address.");
        if (string.Equals(to, from, StringComparison.Ordinal)) return (null, "That is this wallet's own Solana address.");
        if (!SolanaRpc.TryToLamports(amountSol, out var lamports))
            return (null, "Enter a positive amount with at most 9 decimal places.");

        // Developer fee as a second transfer in the same transaction. Dropped if the address is
        // not a valid Solana account, so a misconfiguration never blocks the user's send.
        ulong devFeeLamports = 0;
        string? devFeeTo = null;
        if (!string.IsNullOrWhiteSpace(devFeeAddress) && devFeeAmount > 0 && SolanaKeys.TryDecode(devFeeAddress, out _))
        {
            devFeeLamports = (ulong)decimal.Truncate(devFeeAmount * SolanaRpc.LamportsPerSol);
            if (devFeeLamports > 0) devFeeTo = devFeeAddress.Trim();
        }

        foreach (var server in SolanaNetwork.Servers())
        {
            var host = new Uri(server).Host;
            var balance = await SolanaNetwork.LamportsAsync(server, SolanaRpc.Request("getBalance", from, new { commitment = "confirmed" }), ct);
            if (balance is null) continue;   // this server did not answer; try the next

            var rentMinimum = await SolanaNetwork.LamportsAsync(server, SolanaRpc.Request("getMinimumBalanceForRentExemption", 0), ct);
            var destination = await SolanaNetwork.LamportsAsync(server, SolanaRpc.Request("getBalance", to, new { commitment = "confirmed" }), ct);
            if (rentMinimum is null || destination is null)
                return (null, $"Could not read the destination or the rent minimum from {host}. Nothing was sent.");

            var needed = lamports + devFeeLamports + SolanaRpc.BaseFeeLamports;
            if (balance < needed)
            {
                return (null,
                    $"Not enough SOL: the balance is {SolanaRpc.Sol(balance.Value)} SOL and this needs " +
                    $"{SolanaRpc.Sol(needed)} SOL including the fee.");
            }

            // An account either holds the rent minimum or nothing at all; the network refuses a
            // transfer that would leave one in between.
            if (destination == 0 && lamports < rentMinimum)
            {
                return (null,
                    $"The destination has no SOL yet, so the first transfer to it must be at least " +
                    $"{SolanaRpc.Sol(rentMinimum.Value)} SOL — the minimum a Solana account must hold.");
            }

            var left = balance.Value - needed;
            if (left > 0 && left < rentMinimum)
            {
                return (null,
                    $"This would leave {SolanaRpc.Sol(left)} SOL, below the {SolanaRpc.Sol(rentMinimum.Value)} SOL a Solana " +
                    "account must keep. Send a little less, or everything.");
            }

            return (new SolSendQuote(from, to, amountSol, lamports, SolanaRpc.ToSol(SolanaRpc.BaseFeeLamports),
                devFeeTo, devFeeLamports, server), null);
        }

        return (null, "No Solana server answered. Check your connection (or Tor). Nothing was sent.");
    }

    public async Task<SolSendOutcome> SignAndBroadcastAsync(SolSendQuote quote, byte[] privateKey, CancellationToken ct = default)
    {
        if (!SolanaKeys.TryDecode(quote.From, out var fromPub) || !SolanaKeys.TryDecode(quote.To, out var toPub))
            return new SolSendOutcome(SolSubmitOutcome.Rejected, null, "Invalid address. Nothing was sent.");

        var publicKey = new byte[Ed25519.PublicKeySize];
        Ed25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
        if (!publicKey.AsSpan().SequenceEqual(fromPub))
            return new SolSendOutcome(SolSubmitOutcome.Rejected, null, "Key does not match the sending address — refusing to sign.");

        byte[]? feePub = null;
        if (quote.DevFeeTo is not null && quote.DevFeeLamports > 0 && SolanaKeys.TryDecode(quote.DevFeeTo, out var fp)) feePub = fp;

        var instructions = new List<SolanaInstruction> { SystemTransfer(fromPub, toPub, quote.Lamports) };
        if (feePub is not null) instructions.Add(SystemTransfer(fromPub, feePub, quote.DevFeeLamports));

        return await SolanaNetwork.SignSubmitAndFollowAsync(quote.Server, fromPub, instructions, privateKey, ct);
    }

    /// <summary>The System Program's Transfer: instruction 2, then the lamports, both little-endian.</summary>
    public static SolanaInstruction SystemTransfer(byte[] from, byte[] to, ulong lamports) => new(
        SystemProgramId,
        [new SolanaAccountMeta(from, true, true), new SolanaAccountMeta(to, false, true)],
        [.. BitConverter.GetBytes(2u), .. BitConverter.GetBytes(lamports)]);

    /// <summary>
    /// The legacy message for a transfer (and the developer fee, when there is one) — the layout
    /// <c>SolanaMessageTests</c> pins field by field.
    /// </summary>
    public static byte[] BuildTransferMessage(
        byte[] fromPub, byte[] toPub, ulong lamports, byte[] blockhash,
        byte[]? feePub = null, ulong feeLamports = 0)
    {
        var instructions = new List<SolanaInstruction> { SystemTransfer(fromPub, toPub, lamports) };
        if (feePub is not null && feeLamports > 0) instructions.Add(SystemTransfer(fromPub, feePub, feeLamports));
        return SolanaMessage.Compile(fromPub, instructions, blockhash);
    }
}

/// <summary>
/// Solana JSON-RPC as the senders use it: the user's chosen server or the listed ones, through
/// <see cref="PublicHttp"/>, and the one path every signed transaction takes — sign, submit once,
/// follow the signature.
/// </summary>
internal static class SolanaNetwork
{
    public const string DefaultRpc = "https://api.mainnet-beta.solana.com";

    private static HttpClient Read => PublicHttp.For(PublicHttp.NetworkPurpose.ChainData);
    private static HttpClient Submit => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    public static IReadOnlyList<string> Servers() => ChainEndpoints.Candidates("SOL", DefaultRpc);

    /// <summary>A call's result, its error message, or neither when the server did not answer.</summary>
    public static async Task<(JsonElement? Result, string? Error)> CallAsync(
        string server, object request, CancellationToken ct, bool broadcast = false)
    {
        try
        {
            using var res = await (broadcast ? Submit : Read).PostAsJsonAsync(server, request, ct);
            var text = await res.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(text);
            var (result, error) = SolanaRpc.Unwrap(doc.RootElement);
            return (result?.Clone(), error);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return (null, null);
        }
    }

    public static async Task<ulong?> LamportsAsync(string server, object request, CancellationToken ct)
    {
        var (result, _) = await CallAsync(server, request, ct);
        return result is { } r ? SolanaRpc.ParseLamports(r) : null;
    }

    /// <summary>
    /// Signs the instructions with the fee payer's key against a fresh blockhash, submits once, and
    /// follows the signature: confirmed and successful, confirmed and failed, or never landed before
    /// the blockhash's last valid height — which is final, because after it the transaction can never
    /// be included. Anything else is said as it is, never offered as a retry.
    /// </summary>
    public static async Task<SolSendOutcome> SignSubmitAndFollowAsync(
        string server, byte[] feePayer, IReadOnlyList<SolanaInstruction> instructions, byte[] privateKey, CancellationToken ct)
    {
        var (hashResult, _) = await CallAsync(server, SolanaRpc.Request("getLatestBlockhash", new { commitment = "confirmed" }), ct);
        if ((hashResult is { } hr ? SolanaRpc.ParseBlockhash(hr) : null) is not { } recent)
            return new SolSendOutcome(SolSubmitOutcome.Rejected, null, "Could not fetch a recent blockhash. Nothing was sent.");

        var message = SolanaMessage.Compile(feePayer, instructions, recent.Blockhash);
        var signature = new byte[Ed25519.SignatureSize];
        Ed25519.Sign(privateKey, 0, message, 0, message.Length, signature, 0);
        var id = Encoders.Base58.EncodeData(signature);
        var tx = SolanaMessage.Transaction(signature, message);

        // The node simulates it first ("preflight"); a refusal there means nothing was sent or charged.
        var (sent, refused) = await CallAsync(server, SolanaRpc.Request("sendTransaction",
            Convert.ToBase64String(tx), new { encoding = "base64", preflightCommitment = "confirmed" }), ct, broadcast: true);
        if (refused is not null) return new SolSendOutcome(SolSubmitOutcome.Rejected, null, refused);
        if (sent is { ValueKind: JsonValueKind.String } s && s.GetString() != id)
            return new SolSendOutcome(SolSubmitOutcome.Unknown, id,
                $"The server answered with a different transaction id. Check {id} on an explorer before sending again.");

        for (var attempt = 0; attempt < 45; attempt++)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), ct);
            var (status, _) = await CallAsync(server, SolanaRpc.Request("getSignatureStatuses",
                new[] { id }, new { searchTransactionHistory = false }), ct);
            if (status is null) continue;

            var (outcome, reason) = SolanaRpc.ParseSignatureStatus(status.Value);
            if (outcome is SolSubmitOutcome.Included or SolSubmitOutcome.FailedFeeCharged)
                return new SolSendOutcome(outcome, id, reason);

            var height = await LamportsAsync(server, SolanaRpc.Request("getBlockHeight", new { commitment = "confirmed" }), ct);
            if (height > recent.LastValidBlockHeight)
            {
                // Past its last valid height: ask once more, across history, before calling it gone.
                var (final, _) = await CallAsync(server, SolanaRpc.Request("getSignatureStatuses",
                    new[] { id }, new { searchTransactionHistory = true }), ct);
                var (last, lastReason) = final is { } f ? SolanaRpc.ParseSignatureStatus(f) : (SolSubmitOutcome.Unknown, null);
                return last switch
                {
                    SolSubmitOutcome.Included or SolSubmitOutcome.FailedFeeCharged => new SolSendOutcome(last, id, lastReason),
                    SolSubmitOutcome.Pending => new SolSendOutcome(SolSubmitOutcome.Rejected, id,
                        "The network did not include the transaction before its blockhash expired. Nothing was sent."),
                    _ => new SolSendOutcome(SolSubmitOutcome.Unknown, id,
                        $"Could not tell whether the transaction landed. Check {id} on an explorer before sending again."),
                };
            }
        }

        return new SolSendOutcome(SolSubmitOutcome.Unknown, id,
            $"The network has not confirmed the transaction yet. It can only land within about a minute of sending, " +
            $"never after. Check {id} on an explorer before sending again.");
    }
}
