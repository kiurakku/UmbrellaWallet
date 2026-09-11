using Umbrella.Wallet.Core.Safety;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Slowing down somebody typing guesses at an unlocked machine.
///
/// The threat is specific and so is the answer. Without a delay, a person who finds the wallet open
/// can try passwords as fast as they can type. With one, the tenth guess costs minutes and the
/// twentieth costs the cap — which turns a coffee break into an impossibility.
///
/// The tests below pin both directions, because both failures are real: too little delay leaves the
/// keyboard attack open, and too much would lock the owner out of their own money over a forgotten
/// password. The wallet's job is to slow an attacker, not to hold the vault hostage.
/// </summary>
public sealed class UnlockThrottleTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 11, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void The_first_few_mistakes_cost_nothing(int failures)
    {
        // Mistyping a long password twice is ordinary. Punishing it would train people to choose a
        // shorter one, which is the opposite of what this is for.
        Assert.Equal(TimeSpan.Zero, UnlockThrottle.DelayAfter(failures));
        Assert.False(UnlockThrottle.IsBlocked(failures, T0, T0));
    }

    [Fact]
    public void The_delay_doubles_once_the_free_attempts_are_gone()
    {
        Assert.Equal(TimeSpan.FromSeconds(5), UnlockThrottle.DelayAfter(4));
        Assert.Equal(TimeSpan.FromSeconds(10), UnlockThrottle.DelayAfter(5));
        Assert.Equal(TimeSpan.FromSeconds(20), UnlockThrottle.DelayAfter(6));
        Assert.Equal(TimeSpan.FromSeconds(40), UnlockThrottle.DelayAfter(7));
    }

    [Fact]
    public void A_dozen_guesses_already_costs_minutes()
    {
        // The point of the curve: casual guessing stops being worth anybody's afternoon.
        Assert.True(UnlockThrottle.DelayAfter(12) >= TimeSpan.FromMinutes(2));
    }

    [Fact]
    public void The_delay_never_grows_past_the_cap()
    {
        // A forgotten password must not lock the owner out for a day. Checked well past the doubling
        // range, including values where a naive shift would overflow.
        foreach (var failures in new[] { 20, 40, 100, 10_000, int.MaxValue })
        {
            Assert.Equal(UnlockThrottle.MaxDelay, UnlockThrottle.DelayAfter(failures));
        }
    }

    [Fact]
    public void Waiting_out_the_delay_unblocks_the_next_attempt()
    {
        var failures = 5;                               // 10 seconds
        Assert.True(UnlockThrottle.IsBlocked(failures, T0, T0.AddSeconds(9)));
        Assert.False(UnlockThrottle.IsBlocked(failures, T0, T0.AddSeconds(10)));
        Assert.False(UnlockThrottle.IsBlocked(failures, T0, T0.AddMinutes(5)));
    }

    [Fact]
    public void Remaining_counts_down_rather_than_restarting()
    {
        var failures = 6;                               // 20 seconds
        Assert.Equal(TimeSpan.FromSeconds(20), UnlockThrottle.Remaining(failures, T0, T0));
        Assert.Equal(TimeSpan.FromSeconds(5), UnlockThrottle.Remaining(failures, T0, T0.AddSeconds(15)));
        Assert.Equal(TimeSpan.Zero, UnlockThrottle.Remaining(failures, T0, T0.AddSeconds(30)));
    }

    [Fact]
    public void A_clock_that_jumps_backwards_does_not_extend_the_lockout()
    {
        // Daylight saving, an NTP correction, or a user changing the system clock. None of those are
        // an attack, and none of them should leave somebody waiting longer for their own wallet.
        var remaining = UnlockThrottle.Remaining(6, lastFailure: T0, now: T0.AddHours(-3));

        Assert.Equal(UnlockThrottle.DelayAfter(6), remaining);
        Assert.True(remaining <= UnlockThrottle.MaxDelay);
    }

    [Fact]
    public void The_wait_is_described_in_something_a_person_reads()
    {
        Assert.Equal("5s", UnlockThrottle.Describe(TimeSpan.FromSeconds(5)));
        Assert.Equal("1s", UnlockThrottle.Describe(TimeSpan.FromMilliseconds(200)));   // rounds up, never to "0s"
        Assert.Equal("2m", UnlockThrottle.Describe(TimeSpan.FromSeconds(61)));
        Assert.Equal(string.Empty, UnlockThrottle.Describe(TimeSpan.Zero));
        Assert.Equal(string.Empty, UnlockThrottle.Describe(TimeSpan.FromSeconds(-5)));
    }

    [Fact]
    public void The_cap_is_short_enough_to_be_survivable_and_long_enough_to_matter()
    {
        // Stated as a range rather than a number so the intent survives a future tweak: long enough
        // that guessing is pointless, short enough that the owner is never locked out meaningfully.
        Assert.True(UnlockThrottle.MaxDelay >= TimeSpan.FromMinutes(5));
        Assert.True(UnlockThrottle.MaxDelay <= TimeSpan.FromMinutes(30));
    }
}
