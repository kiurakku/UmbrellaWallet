using System.Reflection;

namespace Umbrella.Wallet.Core.Tests;

/// <summary>
/// Tests that reach the real internet must be marked so CI can skip them.
///
/// CI filters on <c>Category!=Live</c>. A test class that makes real requests without that trait runs
/// on every pull request: it hammers free public explorers from GitHub runners, takes seconds per
/// call, and fails the build whenever one of those services rate-limits the runner — a flake with
/// nothing to do with the change being tested.
///
/// The whole suite was taken offline for precisely that reason. Then a new file of live tests was
/// added and named after the *other* convention — its own filter was by class name, not by trait — so
/// CI ran it anyway. Nobody noticed, because it happened to pass.
///
/// The rule enforced here is simple enough to keep: a test class whose name says it is live must
/// carry the trait that makes it skippable.
/// </summary>
public sealed class LiveTestTraitTests
{
    [Fact]
    public void Every_live_test_class_carries_the_category_that_lets_ci_skip_it()
    {
        var offenders = typeof(LiveTestTraitTests).Assembly
            .GetTypes()
            // This class is ABOUT live tests, not one of them: it touches no network and must not
            // require the trait it enforces. Named rather than pattern-matched around, so the
            // exception is visible instead of hidden in a cleverer rule.
            .Where(t => t != typeof(LiveTestTraitTests))
            .Where(t => t.IsClass && t.Name.Contains("Live", StringComparison.Ordinal))
            .Where(t => t.GetMethods().Any(m => m.GetCustomAttributes()
                .Any(a => a.GetType().Name is "FactAttribute" or "TheoryAttribute")))
            .Where(t => !HasLiveTrait(t))
            .Select(t => t.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToList();

        Assert.Empty(offenders);
    }

    [Fact]
    public void The_trait_value_is_spelled_the_way_the_ci_filter_expects()
    {
        // "live" or "Live" are not the same string to a --filter expression. One typo and the class
        // silently goes back to running in CI.
        var live = typeof(LiveTestTraitTests).Assembly
            .GetTypes()
            .Where(t => t.IsClass && HasAnyCategoryTrait(t))
            .ToList();

        Assert.NotEmpty(live);

        foreach (var type in live)
        {
            foreach (var value in CategoryValues(type))
            {
                Assert.Equal("Live", value);
            }
        }
    }

    private static bool HasLiveTrait(Type type) =>
        CategoryValues(type).Any(v => v.Equals("Live", StringComparison.Ordinal));

    private static bool HasAnyCategoryTrait(Type type) => CategoryValues(type).Any();

    /// <summary>
    /// The values of every <c>[Trait("Category", …)]</c> on a type.
    ///
    /// Read reflectively rather than by referencing xunit's attribute type, because TraitAttribute
    /// carries its data as plain constructor arguments and that is the only place it is readable.
    /// </summary>
    private static IEnumerable<string> CategoryValues(Type type) =>
        type.GetCustomAttributesData()
            .Where(a => a.AttributeType.Name == "TraitAttribute")
            .Where(a => a.ConstructorArguments.Count == 2)
            .Where(a => (a.ConstructorArguments[0].Value as string) == "Category")
            .Select(a => (a.ConstructorArguments[1].Value as string) ?? string.Empty);
}
