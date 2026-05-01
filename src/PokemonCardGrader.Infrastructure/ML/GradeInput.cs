using Microsoft.ML.Data;

namespace PokemonCardGrader.Infrastructure.ML;

public sealed class GradeInput
{
    [LoadColumn(0)] public float CenteringLRFront { get; set; }
    [LoadColumn(1)] public float CenteringTBFront { get; set; }
    [LoadColumn(2)] public float CenteringLRBack { get; set; }
    [LoadColumn(3)] public float CenteringTBBack { get; set; }
    [LoadColumn(4)] public float Corners { get; set; }
    [LoadColumn(5)] public float Edges { get; set; }
    [LoadColumn(6)] public float Surface { get; set; }
    [LoadColumn(7), ColumnName("Label")] public float Grade { get; set; }
}
