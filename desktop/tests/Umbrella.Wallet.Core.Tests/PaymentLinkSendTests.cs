using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// A pasted <c>bitcoin:</c> link on the Send screen (roadmap P2.2): the fields it fills, the PayJoin
/// note it shows, and — the safety property — that editing the destination drops the endpoint, so a
/// PayJoin is never attempted against an address the link did not name.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class PaymentLinkSendTests : IDisposable
{
    private const string Addr = "bc1qw508d6qejxtdg4y5r3zarvary0c5xw7kv8f3t4";
    private const string OtherAddr = "bc1qar0srrr7xfkvy5l643lydnw9re59gtzzwf5mdq";

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"umbrella-bip21-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch { }
    }

    [Fact]
    public void A_pasted_link_fills_the_address_and_amount_and_selects_Bitcoin()
    {
        var vm = NewViewModel();

        vm.SendTo = $"bitcoin:{Addr}?amount=0.015&pj=https%3A%2F%2Fpay.example.com%2Fpj";

        Assert.Equal(Addr, vm.SendTo);
        Assert.Equal("0.015", vm.SendAmount);
        Assert.Equal("BTC", vm.SendChain);
        Assert.True(vm.HasSendPayjoinNote);
    }

    [Fact]
    public void Editing_the_destination_forgets_the_PayJoin_endpoint()
    {
        var vm = NewViewModel();
        vm.SendTo = $"bitcoin:{Addr}?pj=https%3A%2F%2Fpay.example.com%2Fpj";
        Assert.True(vm.HasSendPayjoinNote);

        vm.SendTo = OtherAddr;

        Assert.False(vm.HasSendPayjoinNote);
    }

    [Fact]
    public void An_insecure_endpoint_is_named_as_ignored_and_the_note_goes_with_the_address()
    {
        var vm = NewViewModel();
        vm.SendTo = $"bitcoin:{Addr}?amount=0.01&pj=http%3A%2F%2Fpay.example.com%2Fpj";

        Assert.Equal(Addr, vm.SendTo);
        Assert.True(vm.HasSendPayjoinNote);   // says it will NOT be used — not silence

        vm.SendTo = OtherAddr;
        Assert.False(vm.HasSendPayjoinNote);
    }

    [Fact]
    public void A_refused_link_stays_visible_with_the_reason()
    {
        var vm = NewViewModel();
        var link = $"bitcoin:{Addr}?amount=0,5";

        vm.SendTo = link;

        Assert.Equal(link, vm.SendTo);       // not silently rewritten into something else
        Assert.NotEmpty(vm.SendError);
        Assert.Equal(string.Empty, vm.SendAmount);
    }
}
