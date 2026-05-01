using Microsoft.ML.Data;

namespace PokemonCardGrader.Infrastructure.ML;

public sealed class GradePrediction
{
    [ColumnName("Score")]
    public float Score { get; set; }
}
