using System.Numerics;
using Umbrella.Wallet.Core.Ton;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Roadmap N.3 — the jetton transfer body, pinned to <c>@ton/core</c>'s own output.
///
/// A jetton moves by a message to the sender's OWN jetton wallet, which then credits the recipient's.
/// The layout is fixed by TEP-74 and there is no forgiving part of it: a wrong bit in the address, a
/// coins field written as a plain integer, a forward payload flagged inline when it is a reference —
/// each produces a message the jetton wallet rejects, or worse, one it accepts and interprets
/// differently from what the user was shown.
///
/// So the hashes below come from the reference library, generated for these exact inputs (raw
/// addresses, so no checksum can be wrong in the fixture itself):
///
/// <code>
/// beginCell().storeUint(0x0f8a7ea5,32).storeUint(0,64).storeCoins(amount)
///   .storeAddress(dest).storeAddress(response).storeBit(0).storeCoins(fwd)…
/// </code>
///
/// A cell hash covers every bit and every reference, so matching it proves the body this wallet
/// builds is the body the reference builds — not merely that it looks similar.
/// </summary>
public sealed class TonJettonTransferTests
{
    private static byte[] Repeat(byte value) => Enumerable.Repeat(value, 32).ToArray();

    /// <summary>0:1111… — the recipient's wallet in the reference run.</summary>
    private static byte[] Destination => Repeat(0x11);

    /// <summary>0:2222… — where leftover TON goes back to (the sender).</summary>
    private static byte[] Response => Repeat(0x22);

    [Fact]
    public void A_plain_transfer_body_matches_the_reference_library()
    {
        var body = TonJettonTransfer.BuildTransferBody(
            amountUnits: new BigInteger(1_500_000),
            destinationWorkchain: 0, destinationHash: Destination,
            responseWorkchain: 0, responseHash: Response,
            forwardNano: new BigInteger(1));

        Assert.Equal(
            "e2dfd375dd1608e087a15cd9cadd9bced15cb76ac9bd49878427d518a3cbbad9",
            Convert.ToHexString(body.Hash()).ToLowerInvariant());
    }

    [Fact]
    public void A_transfer_with_a_comment_matches_the_reference_library()
    {
        var body = TonJettonTransfer.BuildTransferBody(
            amountUnits: new BigInteger(250),
            destinationWorkchain: 0, destinationHash: Destination,
            responseWorkchain: 0, responseHash: Response,
            forwardNano: new BigInteger(50_000_000),
            comment: "hello");

        Assert.Equal(
            "c14685f057b53c892471759a5e93d759857e597e92df03fbae4853597b57b208",
            Convert.ToHexString(body.Hash()).ToLowerInvariant());
    }

    /// <summary>
    /// The probe. If the builder ignored its inputs — or the fixtures were copied from each other —
    /// these two bodies would hash the same, and both tests above would pass while proving nothing.
    /// </summary>
    [Fact]
    public void A_different_amount_is_a_different_body()
    {
        var one = TonJettonTransfer.BuildTransferBody(
            new BigInteger(1), 0, Destination, 0, Response, BigInteger.One);
        var two = TonJettonTransfer.BuildTransferBody(
            new BigInteger(2), 0, Destination, 0, Response, BigInteger.One);

        Assert.NotEqual(Convert.ToHexString(one.Hash()), Convert.ToHexString(two.Hash()));
    }

    /// <summary>
    /// Sending to the wrong address must be a different message, obviously — but this also pins that
    /// the destination and the response address are not interchangeable. Swapping them would return
    /// the change to the recipient and the tokens to the sender.
    /// </summary>
    [Fact]
    public void The_destination_and_the_response_address_are_not_interchangeable()
    {
        var normal = TonJettonTransfer.BuildTransferBody(
            new BigInteger(10), 0, Destination, 0, Response, BigInteger.One);
        var swapped = TonJettonTransfer.BuildTransferBody(
            new BigInteger(10), 0, Response, 0, Destination, BigInteger.One);

        Assert.NotEqual(Convert.ToHexString(normal.Hash()), Convert.ToHexString(swapped.Hash()));
    }

    [Fact]
    public void A_non_positive_amount_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => TonJettonTransfer.BuildTransferBody(
            BigInteger.Zero, 0, Destination, 0, Response, BigInteger.One));
    }

    /// <summary>
    /// Jetton decimals vary — 9 for most, 6 for USD₮ on TON — so the scaling has to be exact and an
    /// over-precise amount has to be refused rather than rounded.
    /// </summary>
    [Fact]
    public void Amounts_scale_exactly_and_refuse_what_the_jetton_cannot_hold()
    {
        Assert.Equal(new BigInteger(1_000_000_000), TonJettonTransfer.ToUnits(1m, 9));
        Assert.Equal(new BigInteger(1_000_000), TonJettonTransfer.ToUnits(1m, 6));

        Assert.Throws<ArgumentException>(() => TonJettonTransfer.ToUnits(0.0000001m, 6));
    }
}
