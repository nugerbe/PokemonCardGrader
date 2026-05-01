using PokemonCardGrader.Domain.Enums;

namespace PokemonCardGrader.Domain.ValueObjects;

public sealed record GradeResult
{
    public required GradingCompany Company { get; init; }
    public required double Grade { get; init; }
    public required Dictionary<string, double> SubGrades { get; init; }
    public required double Confidence { get; init; }
    public required string Label { get; init; }
    public required bool IsRuleBased { get; init; }

    /// <summary>
    /// Descriptive label for the grade (e.g., "Gem Mint", "Pristine", "Near Mint").
    /// </summary>
    public string GradeLabel => Company switch
    {
        GradingCompany.PSA => Grade switch
        {
            10 => "Gem Mint",
            9 => "Mint",
            8 => "NM-MT",
            7 => "Near Mint",
            6 => "EX-MT",
            5 => "Excellent",
            4 => "VG-EX",
            3 => "VG",
            2 => "Good",
            1 => "Poor",
            _ => "N/A"
        },
        GradingCompany.BGS => Grade switch
        {
            10 => Label,
            >= 9.5 => "Gem Mint",
            >= 9 => "Mint",
            >= 8.5 => "NM-MT+",
            >= 8 => "NM-MT",
            _ => $"{Grade:F1}"
        },
        GradingCompany.CGC => Grade switch
        {
            10 when Label == "Perfect" => "Perfect",
            10 when Label == "Pristine" => "Pristine",
            10 => "Gem Mint",
            >= 9.5 => "Gem Mint",
            >= 9 => "Mint",
            _ => $"{Grade:F1}"
        },
        _ => $"{Grade:F1}"
    };
}
