using System.Text;
using Org.BouncyCastle.Math.EC.Rfc8032;
using Umbrella.Wallet.Core.Chains;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Solana program-derived addresses and the message compiler.
///
/// The addresses are the Solana SDK's own <c>test_create_program_address</c> vectors
/// (anza-xyz/solana-sdk, address/src/lib.rs). Each one is a SHA-256 that landed OFF the curve, so
/// matching them pins the hash layout and the on-curve test together.
/// </summary>
public sealed class SolanaKeysTests
{
    private static readonly byte[] BpfLoader = Decode("BPFLoaderUpgradeab1e11111111111111111111111");

    private static byte[] Decode(string address)
    {
        Assert.True(SolanaKeys.TryDecode(address, out var key));
        return key;
    }

    public static TheoryData<string, string> SdkVectors => new()
    {
        { "empty|01", "BwqrghZA2htAcqq8dzP1WDAhTXYTYWj7CHxF5j7TDBAe" },
        { "☉|00", "13yWmRpaTR4r5nAktwLqMpRNr28tnVUZw26rTvPSSB19" },
        { "Talking|Squirrels", "2fnQrngrQT4SeLcdToJAD96phoEjNL2man2kfRLCASVk" },
        { "SeedPubey|01", "976ymqVnfE32QFe6NfGDctSvVa36LWnvYxhU6G2232YL" },
    };

    private static List<byte[]> Seeds(string spec) => spec switch
    {
        "empty|01" => [[], [1]],
        "☉|00" => [Encoding.UTF8.GetBytes("☉"), [0]],
        "Talking|Squirrels" => [Encoding.ASCII.GetBytes("Talking"), Encoding.ASCII.GetBytes("Squirrels")],
        "SeedPubey|01" => [Decode("SeedPubey1111111111111111111111111111111111"), [1]],
        _ => throw new ArgumentException(spec),
    };

    [Theory]
    [MemberData(nameof(SdkVectors))]
    public void Program_addresses_match_the_solana_sdk(string seeds, string expected)
    {
        var address = SolanaKeys.CreateProgramAddress(Seeds(seeds), BpfLoader);
        Assert.NotNull(address);
        Assert.Equal(expected, SolanaKeys.Encode(address));
        Assert.False(SolanaKeys.IsOnCurve(address));
    }

    [Fact]
    public void Seeds_the_sdk_refuses_are_refused()
    {
        Assert.Null(SolanaKeys.CreateProgramAddress([new byte[33]], BpfLoader));                         // a seed over 32 bytes
        Assert.NotNull(SolanaKeys.CreateProgramAddress([new byte[32]], BpfLoader));                      // exactly 32 is fine
        Assert.Null(SolanaKeys.CreateProgramAddress(Enumerable.Range(1, 17).Select(i => new[] { (byte)i }).ToList(), BpfLoader));
    }

    [Fact]
    public void Finding_an_address_is_creating_one_with_the_highest_bump_that_works()
    {
        var seeds = new List<byte[]> { Encoding.ASCII.GetBytes("Lil'"), Encoding.ASCII.GetBytes("Bits") };
        var (address, bump) = SolanaKeys.FindProgramAddress(seeds, BpfLoader);
        Assert.Equal(address, SolanaKeys.CreateProgramAddress([.. seeds, [bump]], BpfLoader));
        for (var higher = bump + 1; higher <= 255; higher++)
            Assert.Null(SolanaKeys.CreateProgramAddress([.. seeds, [(byte)higher]], BpfLoader));
    }

    [Fact]
    public void Real_public_keys_are_on_the_curve()
    {
        for (byte i = 1; i <= 20; i++)
        {
            var seed = Enumerable.Repeat(i, 32).ToArray();
            var pub = new byte[32];
            Ed25519.GeneratePublicKey(seed, 0, pub, 0);
            Assert.True(SolanaKeys.IsOnCurve(pub));
        }
    }

    [Fact]
    public void Addresses_round_trip_and_nothing_else_decodes()
    {
        var key = Decode("EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v");
        Assert.Equal("EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v", SolanaKeys.Encode(key));
        Assert.False(SolanaKeys.TryDecode("0OIl", out _));                          // not base58
        Assert.False(SolanaKeys.TryDecode("3yZe7d", out _));                        // too short
        Assert.False(SolanaKeys.TryDecode(null, out _));
    }

    // --- the compiler ------------------------------------------------------------------------------

    private static byte[] Fill(byte b) => Enumerable.Repeat(b, 32).ToArray();

    [Fact]
    public void Accounts_are_grouped_signers_then_writable_then_read_only()
    {
        var payer = Fill(0x90);
        var readOnly = Fill(0x01);
        var writable = Fill(0x80);
        var program = Fill(0x05);
        var ix = new SolanaInstruction(program,
            [new SolanaAccountMeta(readOnly, false, false), new SolanaAccountMeta(writable, false, true), new SolanaAccountMeta(payer, true, true)],
            [7]);

        var message = SolanaMessage.Compile(payer, [ix], Fill(0xAA));

        Assert.Equal(new byte[] { 1, 0, 2, 4 }, message[..4]);          // 1 signer, 0 read-only signers, 2 read-only, 4 keys
        Assert.Equal(payer, message[4..36]);
        Assert.Equal(writable, message[36..68]);
        Assert.Equal(readOnly, message[68..100]);                         // 0x01… sorts before 0x05…
        Assert.Equal(program, message[100..132]);
        // one instruction: program 3, accounts [2, 1, 0], data [7]
        Assert.Equal(new byte[] { 1, 3, 3, 2, 1, 0, 1, 7 }, message[164..]);
    }

    [Fact]
    public void Lengths_use_the_short_vector_encoding()
    {
        static byte[] Enc(int v) { using var ms = new MemoryStream(); SolanaMessage.WriteCompactU16(ms, v); return ms.ToArray(); }
        Assert.Equal(new byte[] { 0x7F }, Enc(127));
        Assert.Equal(new byte[] { 0x80, 0x01 }, Enc(128));
        Assert.Equal(new byte[] { 0xFF, 0xFF, 0x03 }, Enc(65535));
    }
}
