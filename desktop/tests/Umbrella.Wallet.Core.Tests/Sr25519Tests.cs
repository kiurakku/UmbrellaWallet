using System.Text;
using Umbrella.Wallet.Core.Polkadot;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Merlin transcripts and sr25519 signatures (roadmap N.8, send).
///
/// Merlin: the two fixed challenges in the Go port's tests (gtank/merlin, merlin_test.go) — one short
/// protocol, one that wraps the sponge many times over. sr25519: signatures Polkadot.js made and its
/// own tests verify (polkadot-js/wasm, wasm-crypto/src/rs/sr25519.rs), including one that must NOT
/// verify, and its seed-to-public-key vector. Signatures are randomized, so what this wallet signs is
/// checked by the same verifier.
/// </summary>
public sealed class Sr25519Tests
{
    [Fact]
    public void A_simple_transcript_gives_the_go_ports_challenge()
    {
        var t = new MerlinTranscript("test protocol");
        t.AppendMessage("some label"u8, "some data"u8);
        Assert.Equal("d5a21972d0d5fe320c0d263fac7fffb8145aa640af6e9bca177c03c7efcf0615",
            Convert.ToHexString(t.ChallengeBytes("challenge"u8, 32)).ToLowerInvariant());
    }

    [Fact]
    public void A_transcript_that_wraps_the_sponge_gives_the_go_ports_challenge()
    {
        var t = new MerlinTranscript("test protocol");
        t.AppendMessage("step1"u8, "some data"u8);
        var data = Enumerable.Repeat((byte)99, 1024).ToArray();

        byte[] challenge = [];
        for (var i = 0; i < 32; i++)
        {
            challenge = t.ChallengeBytes("challenge"u8, 32);
            t.AppendMessage("bigdata"u8, data);
            t.AppendMessage("challengedata"u8, challenge);
        }

        Assert.Equal("a8c933f54fae76e3f9bea93648c1308e7dfa2152dd51674ff3ca438351cf003c",
            Convert.ToHexString(challenge).ToLowerInvariant());
    }

    [Fact]
    public void A_seed_expands_to_the_public_key_polkadot_js_derives()
    {
        using var pair = Sr25519.FromMiniSecret(Convert.FromHexString("fac7959dbfe72f052e5a0c3c8d6530f202b02fd8f9f5ca3580ec8deb7797479e"));
        Assert.Equal("46ebddef8cd9bb167dc30878d7113b7e168e6f0646beffd77d69d39bad76b47a", Convert.ToHexString(pair.PublicKey).ToLowerInvariant());
    }

    [Theory]
    [InlineData("I hereby verify that I control 5GrwvaEF5zXb26Fz9rcQpDWS57CtERHpNehXCPcNoHGKutQY",
        "d43593c715fdd31c61141abd04a99fd6822c8558854ccde39a5684e7a56da27d",
        "1037eb7e51613d0dcf5930ae518819c87d655056605764840d9280984e1b7063c4566b55bf292fcab07b369d01095879b50517beca4d26e6a65866e25fec0d83")]
    [InlineData("<Bytes>message to sign</Bytes>",
        "f84d048da2ddae2d9d8fd6763f469566e8817a26114f39408de15547f6d47805",
        "48ce2c90e08651adfc8ecef84e916f6d1bb51ebebd16150ee12df247841a5437951ea0f9d632ca165e6ab391532e75e701be6a1caa88c8a6bcca3511f55b4183")]
    public void Signatures_polkadot_js_made_verify(string message, string publicKey, string signature) =>
        Assert.True(Sr25519.Verify(Convert.FromHexString(signature), Encoding.UTF8.GetBytes(message), Convert.FromHexString(publicKey)));

    [Fact]
    public void A_signature_over_a_different_message_does_not_verify()
    {
        // polkadot-js's own negative case: the wrapped message's signature, checked against the bare one.
        Assert.False(Sr25519.Verify(
            Convert.FromHexString("48ce2c90e08651adfc8ecef84e916f6d1bb51ebebd16150ee12df247841a5437951ea0f9d632ca165e6ab391532e75e701be6a1caa88c8a6bcca3511f55b4183"),
            "message to sign"u8,
            Convert.FromHexString("f84d048da2ddae2d9d8fd6763f469566e8817a26114f39408de15547f6d47805")));
    }

    [Fact]
    public void What_this_wallet_signs_verifies_and_only_for_that_message_and_key()
    {
        using var pair = Sr25519.FromMiniSecret(Convert.FromHexString("fac7959dbfe72f052e5a0c3c8d6530f202b02fd8f9f5ca3580ec8deb7797479e"));
        var message = "this is a message"u8.ToArray();

        var sig1 = Sr25519.Sign(pair, message);
        var sig2 = Sr25519.Sign(pair, message);

        Assert.True(Sr25519.Verify(sig1, message, pair.PublicKey));
        Assert.True(Sr25519.Verify(sig2, message, pair.PublicKey));
        Assert.NotEqual(sig1, sig2);                                          // fresh randomness each time
        Assert.False(Sr25519.Verify(sig1, "this is a massage"u8, pair.PublicKey));

        using var other = Sr25519.FromMiniSecret(new byte[32]);
        Assert.False(Sr25519.Verify(sig1, message, other.PublicKey));

        var tampered = (byte[])sig1.Clone();
        tampered[40] ^= 1;
        Assert.False(Sr25519.Verify(tampered, message, pair.PublicKey));

        var unmarked = (byte[])sig1.Clone();
        unmarked[63] &= 0x7F;                                                 // an ed25519-style signature
        Assert.False(Sr25519.Verify(unmarked, message, pair.PublicKey));
    }

    [Fact]
    public void The_same_randomness_and_message_give_the_same_signature()
    {
        // The nonce comes from the transcript, the secret nonce and the randomness together — with all
        // three fixed, the signature is fixed; with the message changed, it is not.
        using var pair = Sr25519.FromMiniSecret(Convert.FromHexString("fac7959dbfe72f052e5a0c3c8d6530f202b02fd8f9f5ca3580ec8deb7797479e"));
        var fixedRandom = new byte[32];
        Assert.Equal(Sr25519.Sign(pair, "a"u8, randomness: fixedRandom), Sr25519.Sign(pair, "a"u8, randomness: fixedRandom));
        Assert.NotEqual(Sr25519.Sign(pair, "a"u8, randomness: fixedRandom)[..32], Sr25519.Sign(pair, "b"u8, randomness: fixedRandom)[..32]);
    }
}
