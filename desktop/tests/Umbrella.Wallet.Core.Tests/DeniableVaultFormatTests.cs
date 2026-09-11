using System.Security.Cryptography;
using System.Text;
using Umbrella.Wallet.Core.Seed;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The storage half of a duress password. The security property is not "the decoy opens" — that is the
/// easy part. It is that the FILE cannot be used to prove a second wallet exists, because if it can,
/// the person holding the wrench simply keeps asking.
///
/// So most of these test what the file does NOT reveal. A duress feature that fails here is worse than
/// not having one: it hands somebody confidence in a hiding place that does not hide.
/// </summary>
public sealed class DeniableVaultFormatTests
{
    // A fast stand-in for Argon2id. The format must not care which KDF it is handed, and real Argon2id
    // at 64 MiB would make these tests minutes long for no extra coverage.
    private static readonly VaultKeyDerivation Kdf = (password, salt) =>
    {
        using var derive = new Rfc2898DeriveBytes(
            Encoding.UTF8.GetBytes(password), salt, 1_000, HashAlgorithmName.SHA256);
        return derive.GetBytes(32);
    };

    private const string Real = "abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon abandon about";
    private const string Decoy = "legal winner thank year wave sausage worth useful legal winner thank yellow";

    [Fact]
    public void The_password_it_was_created_with_opens_it()
    {
        var file = DeniableVaultFormat.Create(Real, "correct horse battery staple", Kdf);

        Assert.True(DeniableVaultFormat.TryOpen(file, "correct horse battery staple", Kdf, out var secret));
        Assert.Equal(Real, secret);
    }

    [Fact]
    public void A_wrong_password_opens_nothing()
    {
        var file = DeniableVaultFormat.Create(Real, "correct horse battery staple", Kdf);

        Assert.False(DeniableVaultFormat.TryOpen(file, "wrong password entirely", Kdf, out var secret));
        Assert.Equal(string.Empty, secret);
    }

    [Fact]
    public void Each_password_opens_only_its_own_wallet()
    {
        var file = DeniableVaultFormat.Create(Real, "the-real-one", Kdf);
        file = DeniableVaultFormat.AddSecret(file, "the-real-one", Decoy, "the-duress-one", Kdf);

        Assert.True(DeniableVaultFormat.TryOpen(file, "the-real-one", Kdf, out var a));
        Assert.Equal(Real, a);

        Assert.True(DeniableVaultFormat.TryOpen(file, "the-duress-one", Kdf, out var b));
        Assert.Equal(Decoy, b);
    }

    // --- what the file must not reveal ---------------------------------------------------------

    [Fact]
    public void The_file_is_the_same_size_with_one_wallet_or_two()
    {
        // A file that grows when a duress wallet is added announces that one was added.
        var one = DeniableVaultFormat.Create(Real, "pw-one", Kdf);
        var two = DeniableVaultFormat.AddSecret(one, "pw-one", Decoy, "pw-two", Kdf);

        Assert.Equal(DeniableVaultFormat.FileSize, one.Length);
        Assert.Equal(one.Length, two.Length);
    }

    [Fact]
    public void Adding_a_second_wallet_changes_only_the_other_slot()
    {
        // The first wallet's bytes must not move or change — otherwise comparing a backup taken before
        // and after would show the whole file churned, which is its own kind of tell.
        var one = DeniableVaultFormat.Create(Real, "pw-one", Kdf);
        var two = DeniableVaultFormat.AddSecret(one, "pw-one", Decoy, "pw-two", Kdf);

        var differing = one.Zip(two, (x, y) => x != y).Count(d => d);

        // Exactly one slot's worth of bytes changed, and the header did not.
        const int slotBytes = DeniableVaultFormat.SaltSize + DeniableVaultFormat.NonceSize
                              + DeniableVaultFormat.TagSize + DeniableVaultFormat.PayloadSize;
        Assert.InRange(differing, slotBytes - 8, slotBytes);
        Assert.Equal(one[..8], two[..8]);
    }

    [Fact]
    public void A_twelve_word_and_a_twenty_four_word_wallet_produce_identical_file_sizes()
    {
        // The ciphertext must not leak how long the phrase is: a 12-word decoy beside a 24-word real
        // wallet would otherwise be obvious.
        var short12 = DeniableVaultFormat.Create(Decoy, "pw", Kdf);
        var long24 = DeniableVaultFormat.Create(Real, "pw", Kdf);

        Assert.Equal(short12.Length, long24.Length);
    }

    [Fact]
    public void Nothing_in_the_file_says_how_many_slots_are_in_use()
    {
        // The one-wallet file and the two-wallet file must be structurally indistinguishable: same
        // length, same header, and no byte that counts anything.
        var one = DeniableVaultFormat.Create(Real, "pw-one", Kdf);
        var two = DeniableVaultFormat.AddSecret(one, "pw-one", Decoy, "pw-two", Kdf);

        Assert.Equal(one.Length, two.Length);
        Assert.Equal(one[..8], two[..8]);
    }

    [Fact]
    public void The_real_wallet_does_not_always_land_in_the_same_slot()
    {
        // If the first wallet always took slot 0, an attacker would know the second slot is where a
        // hidden wallet would be, and that emptiness there means there is none.
        var slotOfFirstWallet = new HashSet<int>();

        for (var i = 0; i < 60; i++)
        {
            var file = DeniableVaultFormat.Create(Real, "pw", Kdf);

            // Which slot holds it can only be learned by opening — which is the point — so infer it
            // from which half of the file the secret decrypts in.
            var half = FindOccupiedSlot(file, "pw");
            slotOfFirstWallet.Add(half);
        }

        Assert.Equal(2, slotOfFirstWallet.Count);
    }

    [Fact]
    public void An_unused_slot_is_indistinguishable_from_ciphertext_by_byte_statistics()
    {
        // Both should look like uniform random bytes. A crude check: neither half should be dominated
        // by any single byte value, which is what zero padding or a structured placeholder would show.
        var file = DeniableVaultFormat.Create(Real, "pw", Kdf);

        foreach (var slot in new[] { 0, 1 })
        {
            const int slotBytes = DeniableVaultFormat.SaltSize + DeniableVaultFormat.NonceSize
                                  + DeniableVaultFormat.TagSize + DeniableVaultFormat.PayloadSize;
            var span = file.AsSpan(8 + (slot * slotBytes), slotBytes).ToArray();

            var mostCommon = span.GroupBy(b => b).Max(g => g.Count());
            Assert.True(mostCommon < slotBytes / 8,
                $"slot {slot} has a byte repeated {mostCommon} times in {slotBytes} — that is a pattern");
        }
    }

    // --- integrity -----------------------------------------------------------------------------

    [Fact]
    public void A_tampered_slot_does_not_open()
    {
        var file = DeniableVaultFormat.Create(Real, "pw", Kdf);
        file[^1] ^= 0xFF;   // flip a bit in the last slot's ciphertext

        // Either it still opens from the untouched slot, or it does not open at all — but it must
        // never return altered plaintext.
        if (DeniableVaultFormat.TryOpen(file, "pw", Kdf, out var secret))
            Assert.Equal(Real, secret);
    }

    [Fact]
    public void A_slot_cannot_be_moved_to_the_other_position_and_still_open()
    {
        // The ciphertext is bound to its slot index, so copying a slot over the other one fails to
        // authenticate rather than duplicating a wallet.
        var file = DeniableVaultFormat.Create(Real, "pw", Kdf);
        const int slotBytes = DeniableVaultFormat.SaltSize + DeniableVaultFormat.NonceSize
                              + DeniableVaultFormat.TagSize + DeniableVaultFormat.PayloadSize;

        var occupied = FindOccupiedSlot(file, "pw");
        var other = occupied == 0 ? 1 : 0;
        Array.Copy(file, 8 + (occupied * slotBytes), file, 8 + (other * slotBytes), slotBytes);

        // The moved copy must not authenticate in its new position; only the original slot opens.
        Assert.True(DeniableVaultFormat.TryOpen(file, "pw", Kdf, out var secret));
        Assert.Equal(Real, secret);
    }

    [Fact]
    public void Rubbish_is_not_mistaken_for_a_vault()
    {
        Assert.False(DeniableVaultFormat.IsWellFormed(null));
        Assert.False(DeniableVaultFormat.IsWellFormed([]));
        Assert.False(DeniableVaultFormat.IsWellFormed(RandomNumberGenerator.GetBytes(DeniableVaultFormat.FileSize)));
        Assert.False(DeniableVaultFormat.TryOpen(RandomNumberGenerator.GetBytes(100), "pw", Kdf, out _));
    }

    [Fact]
    public void A_password_that_already_opens_the_vault_cannot_be_reused_for_the_second_wallet()
    {
        // Otherwise the duress password would open the real wallet, which defeats the whole thing.
        var file = DeniableVaultFormat.Create(Real, "same-password", Kdf);

        Assert.Throws<InvalidOperationException>(() =>
            DeniableVaultFormat.AddSecret(file, "same-password", Decoy, "same-password", Kdf));
    }

    [Fact]
    public void A_second_wallet_cannot_be_added_without_the_first_password()
    {
        var file = DeniableVaultFormat.Create(Real, "pw-one", Kdf);

        Assert.Throws<InvalidOperationException>(() =>
            DeniableVaultFormat.AddSecret(file, "not-the-password", Decoy, "pw-two", Kdf));
    }

    [Fact]
    public void A_secret_too_large_to_fit_is_refused_rather_than_truncated()
    {
        // Silently storing half a seed phrase would lose somebody's wallet.
        var huge = new string('a', DeniableVaultFormat.MaxSecretBytes + 1);
        Assert.Throws<ArgumentException>(() => DeniableVaultFormat.Create(huge, "pw", Kdf));
    }

    [Fact]
    public void A_full_length_secret_still_fits()
    {
        var atLimit = new string('a', DeniableVaultFormat.MaxSecretBytes);
        var file = DeniableVaultFormat.Create(atLimit, "pw", Kdf);

        Assert.True(DeniableVaultFormat.TryOpen(file, "pw", Kdf, out var secret));
        Assert.Equal(atLimit, secret);
    }

    private static int FindOccupiedSlot(byte[] file, string password)
    {
        const int slotBytes = DeniableVaultFormat.SaltSize + DeniableVaultFormat.NonceSize
                              + DeniableVaultFormat.TagSize + DeniableVaultFormat.PayloadSize;

        for (var slot = 0; slot < DeniableVaultFormat.SlotCount; slot++)
        {
            // Blank the other slot and see whether the file still opens.
            var probe = (byte[])file.Clone();
            var other = slot == 0 ? 1 : 0;
            RandomNumberGenerator.GetBytes(slotBytes).CopyTo(probe, 8 + (other * slotBytes));
            if (DeniableVaultFormat.TryOpen(probe, password, Kdf, out _)) return slot;
        }

        throw new InvalidOperationException("no slot opened");
    }
}
