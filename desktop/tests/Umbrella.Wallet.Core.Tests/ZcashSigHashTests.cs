using System.Text.Json;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Zcash signature hash (ZIP-243), against Zcash's own vectors.
///
/// Zcash does not sign the way Bitcoin does: the digest is BLAKE2b-256 personalised with the consensus
/// branch id of the network upgrade, over committed hashes of the prevouts, sequences and outputs. A
/// mistake here does not send the wrong amount — it makes a transaction the network refuses — but it
/// would make sending impossible, so it is pinned to `sighash.json` from the zcash repository, trimmed
/// to the rows whose transactions this wallet could have built (no JoinSplits), with the shielded
/// digests precomputed so the test does not need a Sapling parser.
/// </summary>
public sealed class ZcashSigHashTests
{
    private sealed record Vector(
        JsonElement[] Inputs, JsonElement[] Outputs, uint LockTime, uint ExpiryHeight, long ValueBalance,
        string ScriptCode, int Index, int HashType, uint BranchId, string SigHash,
        string HashShieldedSpends, string HashShieldedOutputs);

    public static TheoryData<int> VectorIndexes
    {
        get
        {
            var data = new TheoryData<int>();
            for (var i = 0; i < Load().Count; i++) data.Add(i);
            return data;
        }
    }

    private static IReadOnlyList<JsonElement> Load()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "zcash-sighash-vectors.json");
        return JsonDocument.Parse(File.ReadAllText(path)).RootElement.EnumerateArray().ToList();
    }

    [Theory]
    [MemberData(nameof(VectorIndexes))]
    public void The_signature_hash_matches_zcashs_own_vectors(int index)
    {
        var v = Load()[index];

        var inputs = v.GetProperty("inputs").EnumerateArray().Select(i => new ZcashInput(
            Convert.FromHexString(i.GetProperty("txid").GetString()!),
            i.GetProperty("index").GetUInt32(),
            0,                                       // the vectors sign with a zero input value
            [])
        {
            Sequence = BitConverter.GetBytes(i.GetProperty("sequence").GetUInt32()),
        }).ToList();

        var outputs = v.GetProperty("outputs").EnumerateArray().Select(o => new ZcashOutput(
            o.GetProperty("value").GetUInt64(),
            Convert.FromHexString(o.GetProperty("script").GetString()!))).ToList();

        var hash = ZcashTransactions.SigHash(
            inputs, outputs,
            v.GetProperty("index").GetInt32(),
            Convert.FromHexString(v.GetProperty("scriptCode").GetString()!),
            v.GetProperty("hashType").GetInt32(),
            v.GetProperty("lockTime").GetUInt32(),
            v.GetProperty("expiryHeight").GetUInt32(),
            v.GetProperty("branchId").GetUInt32(),
            v.GetProperty("valueBalance").GetInt64(),
            Convert.FromHexString(v.GetProperty("hashShieldedSpends").GetString()!),
            Convert.FromHexString(v.GetProperty("hashShieldedOutputs").GetString()!));

        // Zcash prints a uint256 in reverse byte order, which is how the vectors record it.
        Assert.Equal(v.GetProperty("sigHash").GetString(), Convert.ToHexString(hash.Reverse().ToArray()).ToLowerInvariant());
    }

    [Fact]
    public void The_vectors_cover_the_signing_shapes_that_matter()
    {
        var types = Load().Select(v => v.GetProperty("hashType").GetInt32()).ToList();
        Assert.True(types.Count >= 20);
        Assert.Contains(types, t => (t & 0x80) != 0);          // ANYONECANPAY
        Assert.Contains(types, t => (t & 0x1F) == 2);          // NONE
        Assert.Contains(types, t => (t & 0x1F) == 3);          // SINGLE
    }
}
