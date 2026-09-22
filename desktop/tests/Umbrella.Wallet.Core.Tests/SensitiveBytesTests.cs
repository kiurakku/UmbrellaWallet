using Umbrella.Wallet.Core.Security;

namespace Umbrella.Wallet.Core.Tests;

public sealed class SensitiveBytesTests
{
    [Fact]
    public void Clear_overwrites_the_buffer()
    {
        var buf = new byte[] { 1, 2, 3, 9 };
        SensitiveBytes.Clear(buf);
        Assert.All(buf, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Use_clears_even_when_the_callback_throws()
    {
        var buf = new byte[] { 7, 7, 7 };
        Assert.Throws<InvalidOperationException>(() =>
            SensitiveBytes.Use(buf, _ => throw new InvalidOperationException("boom")));
        Assert.All(buf, b => Assert.Equal(0, b));
    }

    [Fact]
    public void Use_returns_the_callback_result()
    {
        var buf = new byte[] { 4, 5 };
        var sum = SensitiveBytes.Use(buf, b => b[0] + b[1]);
        Assert.Equal(9, sum);
        Assert.All(buf, b => Assert.Equal(0, b));
    }
}
