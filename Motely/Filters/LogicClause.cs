namespace Motely.Filters;

/// <summary>
/// How an <c>or</c> combines arm scores. <see cref="Sum"/> totals every arm that matches;
/// <see cref="Max"/> scores only the best arm. AND match semantics stay min-of-children
/// conjunctions — mode does not reopen sum-of-children AND.
/// </summary>
public enum LogicScoreMode
{
    Sum = 0,
    Max = 1,
}

public abstract class LogicClause : MotelyClause
{
    public LogicScoreMode Mode { get; set; } = LogicScoreMode.Sum;

    /// <summary>The document key <c>clauses:</c>. No antes here: each child clause carries its own.</summary>
    public MotelyClause[] Clauses { get; set => field = value ?? []; } = [];
}

[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class AndClause : LogicClause
{
    /// <summary>The document key <c>and:</c>, holding the child clauses.</summary>
    [YamlDotNet.Serialization.YamlMember(Alias = "and")]
    public MotelyClause[]? AndClauses { get => null; set => Clauses = value ?? Clauses; }

    [YamlDotNet.Serialization.YamlIgnore]
    public AndPayload And
    {
        get => new() { Mode = Mode, Clauses = Clauses, Label = Label, Min = Min, Max = Max, Score = Score };
        set
        {
            Mode = value.Mode;
            Clauses = value.Clauses;
            Label = value.Label;
            Min = value.Min;
            Max = value.Max;
            Score = value.Score;
        }
    }

    public override IMotelySeedFilterDesc CreateFilterDesc() =>
        new AndFilterDesc([.. Clauses.Select(c => c.CreateFilterDesc())]);
}

[YamlDotNet.Serialization.YamlSerializable]
public sealed partial class OrClause : LogicClause
{
    /// <summary>The document key <c>or:</c>, holding the child clauses.</summary>
    [YamlDotNet.Serialization.YamlMember(Alias = "or")]
    public MotelyClause[]? OrClauses { get => null; set => Clauses = value ?? Clauses; }

    [YamlDotNet.Serialization.YamlIgnore]
    public OrPayload Or
    {
        get => new() { Mode = Mode, Clauses = Clauses, Label = Label, Min = Min, Max = Max, Score = Score };
        set
        {
            Mode = value.Mode;
            Clauses = value.Clauses;
            Label = value.Label;
            Min = value.Min;
            Max = value.Max;
            Score = value.Score;
        }
    }

    public override IMotelySeedFilterDesc CreateFilterDesc() =>
        new OrFilterDesc([.. Clauses.Select(c => c.CreateFilterDesc())], Min);
}
