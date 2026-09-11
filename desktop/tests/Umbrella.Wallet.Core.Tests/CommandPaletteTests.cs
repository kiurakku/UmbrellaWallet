using Umbrella.Wallet.App.ViewModels;
using Umbrella.Wallet.Infrastructure;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// The Ctrl+K palette has to be fully drivable from the keyboard: ↑/↓ walk the results and Enter runs
/// the highlighted one. Without this, Enter always fired the top row and the arrows did nothing.
/// </summary>
[Collection(SharedAppStateCollection.Name)]
public sealed class CommandPaletteTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), $"umbrella-palette-{Guid.NewGuid():N}");

    private MainViewModel NewViewModel() =>
        new(new EncryptedFileSeedVault(Path.Combine(_directory, "vault.json")));

    [Fact]
    public void Opening_the_palette_highlights_the_first_result()
    {
        var vm = NewViewModel();

        vm.OpenCommandPaletteCommand.Execute(null);

        Assert.True(vm.IsCommandPaletteOpen);
        Assert.NotEmpty(vm.CommandResults);
        Assert.Equal(0, vm.PaletteSelectedIndex);
        Assert.True(vm.CommandResults[0].IsSelected);
        Assert.Single(vm.CommandResults, r => r.IsSelected);
    }

    [Fact]
    public void Arrow_keys_move_the_highlight_and_light_exactly_one_row()
    {
        var vm = NewViewModel();
        vm.OpenCommandPaletteCommand.Execute(null);

        vm.PaletteMoveDownCommand.Execute(null);
        vm.PaletteMoveDownCommand.Execute(null);

        Assert.Equal(2, vm.PaletteSelectedIndex);
        Assert.True(vm.CommandResults[2].IsSelected);
        Assert.Single(vm.CommandResults, r => r.IsSelected);

        vm.PaletteMoveUpCommand.Execute(null);
        Assert.Equal(1, vm.PaletteSelectedIndex);
        Assert.True(vm.CommandResults[1].IsSelected);
    }

    [Fact]
    public void The_highlight_wraps_at_both_ends()
    {
        var vm = NewViewModel();
        vm.OpenCommandPaletteCommand.Execute(null);
        var last = vm.CommandResults.Count - 1;

        // Up from the first row lands on the last…
        vm.PaletteMoveUpCommand.Execute(null);
        Assert.Equal(last, vm.PaletteSelectedIndex);

        // …and down from the last comes back to the first.
        vm.PaletteMoveDownCommand.Execute(null);
        Assert.Equal(0, vm.PaletteSelectedIndex);
    }

    [Fact]
    public async Task Enter_runs_the_highlighted_row_not_always_the_top_one()
    {
        var vm = NewViewModel();
        vm.OpenCommandPaletteCommand.Execute(null);
        vm.PaletteMoveDownCommand.Execute(null);
        var target = vm.CommandResults[vm.PaletteSelectedIndex].Target;

        await vm.RunTopPaletteCommandCommand.ExecuteAsync(null);

        Assert.False(vm.IsCommandPaletteOpen);
        Assert.Equal(target, vm.ActiveSection);
    }

    /// <summary>Typing re-ranks the list, so the highlight must return to the best match.</summary>
    [Fact]
    public void A_new_query_re_homes_the_highlight_on_the_first_match()
    {
        var vm = NewViewModel();
        vm.OpenCommandPaletteCommand.Execute(null);
        vm.PaletteMoveDownCommand.Execute(null);
        vm.PaletteMoveDownCommand.Execute(null);

        vm.CommandQuery = "sett";

        Assert.Equal(0, vm.PaletteSelectedIndex);
        Assert.Equal("Settings", vm.CommandResults[0].Target);
        Assert.True(vm.CommandResults[0].IsSelected);
        Assert.Single(vm.CommandResults, r => r.IsSelected);
    }

    /// <summary>A query that matches nothing must not leave a stale highlight behind to run on Enter.</summary>
    [Fact]
    public async Task Enter_on_an_empty_result_list_does_nothing()
    {
        var vm = NewViewModel();
        vm.OpenCommandPaletteCommand.Execute(null);
        vm.CommandQuery = "zzzz-no-such-command";
        Assert.Empty(vm.CommandResults);

        await vm.RunTopPaletteCommandCommand.ExecuteAsync(null);

        Assert.True(vm.IsCommandPaletteOpen);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true); } catch { }
    }
}
