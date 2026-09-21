using System.Globalization;
using System.Numerics;
using System.Text.Json;

namespace Umbrella.Wallet.Core.Chains;

/// <summary>One SPL token held at a Solana address, summed across every token account for its mint.</summary>
public sealed record SplHolding(string Mint, BigInteger Units, int Decimals)
{
    public decimal Amount => (decimal)Units / (decimal)BigInteger.Pow(10, Decimals);
}

/// <summary>
/// SPL token balances (roadmap N.3).
///
/// A token account says which MINT it holds and how much, and the decimals the mint uses — but not
/// what the token is called; names live in separate metadata anyone can write. So the wallet names only
/// mints it knows, each one checked on-chain to be a real mint with those decimals, and shows every
/// other token by its mint address, marked unverified. On Solana the airdropped lure token is common,
/// and a name the wallet cannot check is exactly what one uses.
///
/// Two token programs exist — the original and Token-2022 (PayPal USD lives on the latter) — and both
/// are read.
/// </summary>
public static class SolanaTokens
{
    public const string TokenProgram = "TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA";
    public const string Token2022Program = "TokenzQdBNbLqP5VEhdkAS6EPFLC1PHnBqCXEpPxuEb";

    /// <summary>
    /// Mints the wallet names. Every entry was checked against the chain (getAccountInfo: a mint, with
    /// these decimals, under this program) before it was written here.
    /// </summary>
    public static IReadOnlyDictionary<string, (string Symbol, string Name)> KnownMints { get; } =
        new Dictionary<string, (string, string)>(StringComparer.Ordinal)
        {
            ["EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v"] = ("USDC", "USD Coin"),
            ["Es9vMFrzaCERmJfrF4H2FYD4KCoNkY11McCe8BenwNYB"] = ("USDT", "Tether"),
            ["2b1kV6DkPAnxd5ixfnxCpjxmKwqjjaYmCZfHsFu24GXo"] = ("PYUSD", "PayPal USD"),
            ["JUPyiwrYJFskUPiHa7hkeR8VUtAeFoSYbKedZNsDvCN"] = ("JUP", "Jupiter"),
            ["DezXAZ8z7PnrnRJjz3wXBoRgixCa6xjnB7YaB1pPB263"] = ("BONK", "Bonk"),
            ["EKpQGSJtjMFqKZ9KQanSqYXRcF8fBopzLHYxdM65zcjm"] = ("WIF", "dogwifhat"),
            ["jtojtomepa8beP8AuQc6eXt5FriJwfFMwQx2v2f9mCL"] = ("JTO", "Jito"),
            ["HZ1JovNiVvGrGNiiYvEozEVgZ58xaU3RKwX8eACQBCt3"] = ("PYTH", "Pyth Network"),
            ["4k3Dyjzvzp8eMZWUXbBCjEvwSkkk59S5iCNLY3QrkX6R"] = ("RAY", "Raydium"),
            ["mSoLzYCxHdYgdzU16g5QSh3i5K3z3KZK7ytfqcJm7So"] = ("mSOL", "Marinade staked SOL"),
            ["J1toso1uCk3RLmjorhTtrVwY9HJ7X8V9yYac6Y7kGCPn"] = ("jitoSOL", "Jito staked SOL"),
        };

    /// <summary>The JSON-RPC body for every token account one program holds for an owner.</summary>
    public static object TokenAccountsRequest(string owner, string program) => new
    {
        jsonrpc = "2.0",
        id = 1,
        method = "getTokenAccountsByOwner",
        @params = new object[] { owner, new { programId = program }, new { encoding = "jsonParsed" } },
    };

    /// <summary>
    /// Holdings from a <c>getTokenAccountsByOwner</c> (jsonParsed) response, summed per mint, zero
    /// balances left out. Null when the answer is not one — an error, or a shape this does not
    /// understand — so the caller can tell "no tokens" from "could not read".
    /// </summary>
    public static IReadOnlyList<SplHolding>? ParseTokenAccounts(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || root.TryGetProperty("error", out _)) return null;
        if (!root.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Object) return null;
        if (!result.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Array) return null;

        var byMint = new Dictionary<string, SplHolding>(StringComparer.Ordinal);
        foreach (var entry in value.EnumerateArray())
        {
            if (!TryInfo(entry, out var info)) return null;

            if (!info.TryGetProperty("mint", out var mintEl) || mintEl.ValueKind != JsonValueKind.String) return null;
            if (!info.TryGetProperty("tokenAmount", out var ta) || ta.ValueKind != JsonValueKind.Object) return null;
            if (!ta.TryGetProperty("amount", out var amountEl) || amountEl.ValueKind != JsonValueKind.String) return null;
            if (!ta.TryGetProperty("decimals", out var decEl) || decEl.ValueKind != JsonValueKind.Number ||
                !decEl.TryGetInt32(out var decimals) || decimals is < 0 or > 28) return null;

            var text = amountEl.GetString();
            if (string.IsNullOrEmpty(text) || !text.All(char.IsAsciiDigit) ||
                !BigInteger.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var units)) return null;
            if (units.IsZero) continue;

            var mint = mintEl.GetString()!;
            if (byMint.TryGetValue(mint, out var held))
            {
                // Two accounts of one mint must agree on its decimals; if not, the answer is corrupt.
                if (held.Decimals != decimals) return null;
                byMint[mint] = held with { Units = held.Units + units };
            }
            else
            {
                byMint[mint] = new SplHolding(mint, units, decimals);
            }
        }

        return byMint.Values.OrderBy(h => h.Mint, StringComparer.Ordinal).ToList();
    }

    private static bool TryInfo(JsonElement entry, out JsonElement info)
    {
        info = default;
        return entry.ValueKind == JsonValueKind.Object &&
               entry.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object &&
               account.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object &&
               data.TryGetProperty("parsed", out var parsed) && parsed.ValueKind == JsonValueKind.Object &&
               parsed.TryGetProperty("info", out info) && info.ValueKind == JsonValueKind.Object;
    }
}
