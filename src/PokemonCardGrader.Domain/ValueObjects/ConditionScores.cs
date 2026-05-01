namespace PokemonCardGrader.Domain.ValueObjects;

public sealed record ConditionScores
{
    public required CenteringMeasurement Centering { get; init; }

    /// <summary>Score from 1-10 in 0.5 increments.</summary>
    public required double Corners { get; init; }

    /// <summary>Score from 1-10 in 0.5 increments.</summary>
    public required double Edges { get; init; }

    /// <summary>Score from 1-10 in 0.5 increments.</summary>
    public required double Surface { get; init; }

    public static ConditionScores Perfect => new()
    {
        Centering = CenteringMeasurement.Perfect,
        Corners = 10,
        Edges = 10,
        Surface = 10
    };
}
