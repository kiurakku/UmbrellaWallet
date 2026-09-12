using System.Net.Http.Json;
using System.Text.Json;
using NBitcoin.DataEncoders;
using Org.BouncyCastle.Math.EC.Rfc8032;

namespace Umbrella.Wallet.Infrastructure.Network;

public sealed record SolSendQuote(
    string From, string To, decimal AmountSol, ulong Lamports, decimal FeeSol,
    string? DevFeeTo = null, ulong DevFeeLamports = 0);

/// <summary>
/// Real Solana transfers. Builds the legacy transaction message by hand (System Program
/// transfer), signs it with ed25519, and submits the base64 transaction to a public RPC.
/// Message serialisation is pinned by a unit test so a malformed transfer can't ship.
/// </summary>
public sealed class SolanaTransactionSender
{
    private const string Rpc = "https://api.mainnet-beta.solana.com";
    private const ulong LamportsPerSol = 1_000_000_000;

    /// <summary>Flat 5000-lamport signature fee for a single-signer transfer.</summary>
    private const ulong FeeLamports = 5_000;

    private static readonly byte[] SystemProgramId = new byte[32]; // all-zero = System Program

    private static HttpClient Http => PublicHttp.For(PublicHttp.NetworkPurpose.Broadcast);

    public async Task<(SolSendQuote? Quote, string? Error)> PrepareAsync(
        string from, string to, decimal amountSol,
        string? devFeeAddress = null, decimal devFeeAmount = 0m, CancellationToken ct = default)
    {
        if (amountSol <= 0) return (null, "Amount must be positive.");
        if (!TryDecodeAddress(to, out _)) return (null, "That is not a valid Solana address.");

        var lamports = (ulong)(amountSol * LamportsPerSol);

        // Developer fee as a second transfer in the same transaction. Dropped if the address is
        // not a valid Solana account, so a misconfiguration never blocks the user's send.
        ulong devFeeLamports = 0;
        string? devFeeTo = null;
        if (!string.IsNullOrWhiteSpace(devFeeAddress) && devFeeAmount > 0 && TryDecodeAddress(devFeeAddress, out _))
        {
            devFeeLamports = (ulong)(devFeeAmount * LamportsPerSol);
            if (devFeeLamports > 0) devFeeTo = devFeeAddress.Trim();
        }

        var balance = await GetBalanceLamportsAsync(from, ct);
        if (balance is null) return (null, "Solana RPC is unreachable — check your connection (or Tor).");
        var needed = lamports + devFeeLamports + FeeLamports;
        if (balance < needed)
        {
            return (null,
                $"Insufficient funds: balance {balance.Value / (decimal)LamportsPerSol:0.#########} SOL, " +
                $"need {needed / (decimal)LamportsPerSol:0.#########} SOL including fee.");
        }

        return (new SolSendQuote(
            from, to, amountSol, lamports, FeeLamports / (decimal)LamportsPerSol, devFeeTo, devFeeLamports), null);
    }

    public async Task<(bool Ok, string? Signature, string? Error)> SignAndBroadcastAsync(
        SolSendQuote quote, byte[] privateKey, CancellationToken ct = default)
    {
        try
        {
            var blockhash = await GetLatestBlockhashAsync(ct);
            if (blockhash is null) return (false, null, "Could not fetch a recent blockhash.");

            if (!TryDecodeAddress(quote.From, out var fromPub) || !TryDecodeAddress(quote.To, out var toPub))
            {
                return (false, null, "Invalid address.");
            }

            var publicKey = new byte[Ed25519.PublicKeySize];
            Ed25519.GeneratePublicKey(privateKey, 0, publicKey, 0);
            if (!publicKey.SequenceEqual(fromPub))
            {
                return (false, null, "Key does not match the sending address — refusing to sign.");
            }

            byte[]? feePub = null;
            if (quote.DevFeeTo is not null && quote.DevFeeLamports > 0 && TryDecodeAddress(quote.DevFeeTo, out var fp))
            {
                feePub = fp;
            }

            var message = BuildTransferMessage(
                fromPub, toPub, quote.Lamports, blockhash,
                feePub, feePub is not null ? quote.DevFeeLamports : 0);

            var signature = new byte[Ed25519.SignatureSize];
            Ed25519.Sign(privateKey, 0, message, 0, message.Length, signature, 0);

            // Wire format: compact-u16 signature count (1) || signature || message
            var tx = new byte[1 + signature.Length + message.Length];
            tx[0] = 1;
            Buffer.BlockCopy(signature, 0, tx, 1, signature.Length);
            Buffer.BlockCopy(message, 0, tx, 1 + signature.Length, message.Length);

            using var res = await Http.PostAsJsonAsync(Rpc, new
            {
                jsonrpc = "2.0",
                id = 1,
                method = "sendTransaction",
                @params = new object[]
                {
                    Convert.ToBase64String(tx),
                    new { encoding = "base64" },
                },
            }, ct);

            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (doc.RootElement.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.String)
            {
                return (true, result.GetString(), null);
            }

            if (doc.RootElement.TryGetProperty("error", out var error))
            {
                var message2 = error.TryGetProperty("message", out var m) ? m.GetString() : "RPC rejected the transaction";
                return (false, null, message2);
            }

            return (false, null, "Broadcast failed.");
        }
        catch (Exception ex)
        {
            return (false, null, $"Send failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Legacy Solana message: header, account keys, blockhash, System-transfer instruction(s).
    /// Without a developer fee: accounts [from (signer, writable), to (writable), systemProgram
    /// (readonly)] and one transfer — byte-identical to the original, pinned layout.
    /// With a developer fee: accounts [from, to, feeTo, systemProgram] and TWO transfers in the
    /// same transaction (from→to, from→feeTo), so there is only one network fee.
    /// </summary>
    public static byte[] BuildTransferMessage(
        byte[] fromPub, byte[] toPub, ulong lamports, byte[] blockhash,
        byte[]? feePub = null, ulong feeLamports = 0)
    {
        var hasFee = feePub is not null && feeLamports > 0;
        using var ms = new MemoryStream();
        // Header: 1 required signature, 0 readonly-signed, 1 readonly-unsigned (the program).
        ms.WriteByte(1);
        ms.WriteByte(0);
        ms.WriteByte(1);

        if (!hasFee)
        {
            // Account keys (compact array of 3).
            ms.WriteByte(3);
            ms.Write(fromPub);
            ms.Write(toPub);
            ms.Write(SystemProgramId);

            ms.Write(blockhash);

            // Instructions (compact array of 1).
            ms.WriteByte(1);
            ms.WriteByte(2);          // program id index → systemProgram (account index 2)
            ms.WriteByte(2);          // account count
            ms.WriteByte(0);          // from
            ms.WriteByte(1);          // to
            ms.WriteByte(12);         // data length: 4-byte instruction + 8-byte lamports
            ms.Write(BitConverter.GetBytes(2u));            // System instruction 2 = Transfer (LE)
            ms.Write(BitConverter.GetBytes(lamports));      // u64 LE
        }
        else
        {
            // Account keys (compact array of 4): systemProgram is now at index 3.
            ms.WriteByte(4);
            ms.Write(fromPub);
            ms.Write(toPub);
            ms.Write(feePub!);
            ms.Write(SystemProgramId);

            ms.Write(blockhash);

            // Instructions (compact array of 2), both System-Program transfers.
            ms.WriteByte(2);

            ms.WriteByte(3);          // program id index → systemProgram (account index 3)
            ms.WriteByte(2);          // account count
            ms.WriteByte(0);          // from
            ms.WriteByte(1);          // to
            ms.WriteByte(12);
            ms.Write(BitConverter.GetBytes(2u));
            ms.Write(BitConverter.GetBytes(lamports));

            ms.WriteByte(3);          // program id index → systemProgram
            ms.WriteByte(2);          // account count
            ms.WriteByte(0);          // from
            ms.WriteByte(2);          // feeTo
            ms.WriteByte(12);
            ms.Write(BitConverter.GetBytes(2u));
            ms.Write(BitConverter.GetBytes(feeLamports));
        }

        return ms.ToArray();
    }

    private static bool TryDecodeAddress(string address, out byte[] key)
    {
        key = Array.Empty<byte>();
        try
        {
            var decoded = Encoders.Base58.DecodeData(address);
            if (decoded.Length != 32) return false;
            key = decoded;
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static async Task<ulong?> GetBalanceLamportsAsync(string address, CancellationToken ct)
    {
        try
        {
            using var res = await Http.PostAsJsonAsync(Rpc, new
            {
                jsonrpc = "2.0", id = 1, method = "getBalance", @params = new object[] { address },
            }, ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
            return result.GetProperty("value").GetUInt64();
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> GetLatestBlockhashAsync(CancellationToken ct)
    {
        try
        {
            using var res = await Http.PostAsJsonAsync(Rpc, new
            {
                jsonrpc = "2.0", id = 1, method = "getLatestBlockhash",
                @params = new object[] { new { commitment = "finalized" } },
            }, ct);
            if (!res.IsSuccessStatusCode) return null;
            using var doc = await JsonDocument.ParseAsync(await res.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("result", out var result)) return null;
            var hash = result.GetProperty("value").GetProperty("blockhash").GetString();
            return hash is null ? null : Encoders.Base58.DecodeData(hash);
        }
        catch
        {
            return null;
        }
    }
}
