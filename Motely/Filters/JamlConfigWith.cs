namespace Motely.Filters;

/// <summary>
/// Finds the player-holdings block a filter writes with <c>with:</c>. The Jamlyzer takes that same
/// block as an argument, so tool calls and result windows can show what the clause actually reads.
/// A filter that writes several blocks uses the first one with content, in must/should/mustNot
/// order, walking inside <c>and:</c> and <c>or:</c> too.
/// </summary>
public static class JamlConfigWith
{
    /// <summary>The first non-empty <c>with:</c> block, or null when the filter writes none.</summary>
    public static MotelyWith? Find(JamlConfig config) =>
        config.Must.Concat(config.Should).Concat(config.MustNot)
            .SelectMany(FindAll)
            .FirstOrDefault(HasContent);

    private static IEnumerable<MotelyWith> FindAll(MotelyClause clause)
    {
        switch (clause)
        {
            case IWithScopedClause scoped:
                yield return scoped.With;
                break;
            case LogicClause logic:
                foreach (var child in logic.Clauses)
                    foreach (var nested in FindAll(child))
                        yield return nested;
                break;
        }
    }

    private static bool HasContent(MotelyWith with) =>
        with.Luck != MotelyLuck.X1 || with.Jokers.Length > 0 || with.Vouchers.Length > 0;
}
