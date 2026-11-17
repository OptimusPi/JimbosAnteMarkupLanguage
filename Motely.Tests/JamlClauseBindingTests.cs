using System.Reflection;

namespace Motely.Tests;

/// <summary>
/// JamlClauseBinding.ClauseTypeByKey is the one hand-kept list of clause types (an interface cannot
/// name its implementations without reflection, which NativeAOT rules out). These keep it honest.
/// </summary>
public class JamlClauseBindingTests
{
    private static readonly Type[] ClauseTypes = typeof(IMotelyClause)
        .Assembly.GetTypes()
        .Where(static t => typeof(IMotelyClause).IsAssignableFrom(t) && t is { IsClass: true, IsAbstract: false })
        .ToArray();

    [Fact]
    public void EveryKeyIsAPropertyOfItsClause()
    {
        var wrong = JamlClauseBinding
            .ClauseTypeByKey.Where(static entry =>
                entry.Value.GetProperty(
                    entry.Key,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase
                ) is not { CanWrite: true }
            )
            .Select(static entry => $"{entry.Key} -> {entry.Value.Name}");

        Assert.Empty(wrong);
    }

    [Fact]
    public void EveryClauseHasAKey()
    {
        var keyed = JamlClauseBinding.ClauseTypeByKey.Values.ToHashSet();

        Assert.Empty(ClauseTypes.Where(t => !keyed.Contains(t)).Select(static t => t.Name));
    }
}
