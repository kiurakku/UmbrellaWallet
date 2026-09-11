namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Test classes that touch process-wide state, kept out of each other's way.
///
/// <c>Fx</c> holds the display currency and its locale in statics, and <c>Loc.Instance</c> is a
/// singleton holding the current language. Constructing a <see cref="App.ViewModels.MainViewModel"/>
/// writes to both. xUnit runs different test CLASSES in parallel, so a test that sets the language to
/// Ukrainian and then asserts on Ukrainian formatting could have an unrelated class set it back to
/// English in between — and it did: adding test files elsewhere shifted the timing enough that
/// <c>Ukrainian_uses_a_space_for_thousands_and_a_comma_for_decimals</c> started failing in the full
/// run while passing on its own.
///
/// Sharing one collection makes xUnit run these serially. It is deliberately NOT an assembly-wide
/// switch: the crypto, parsing and planner tests are pure and should keep running in parallel, which
/// is most of the suite and most of its speed.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class SharedAppStateCollection
{
    public const string Name = "shared app state";
}
