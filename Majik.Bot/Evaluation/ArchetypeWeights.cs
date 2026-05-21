namespace Majik.Bot.Evaluation;

/// <summary>
/// Per-archetype eval weights. See spec section 5 for the table. Each
/// weight is a multiplier on the matching BoardEval component.
///
/// Burn = race plan (LifeDelta dominant). Prowess = tempo (BoardPower
/// dominant). BorosEnergy = midrange (HandSize + Life dominant).
/// </summary>
public sealed record ArchetypeWeights(
    double LifeDelta,
    double BoardPower,
    double BoardToughness,
    double OpponentThreats,
    double ManaSources,
    double HandSize,
    double Tempo,
    double KeyCardInPlay)
{
    public static readonly ArchetypeWeights Burn = new(
        LifeDelta:        3.0,
        BoardPower:       1.5,
        BoardToughness:   0.2,
        OpponentThreats: -1.0,
        ManaSources:      0.5,
        HandSize:         0.5,
        Tempo:            1.0,
        KeyCardInPlay:    2.0);

    public static readonly ArchetypeWeights Prowess = new(
        LifeDelta:        1.0,
        BoardPower:       2.0,
        BoardToughness:   0.5,
        OpponentThreats: -2.0,
        ManaSources:      1.0,
        HandSize:         0.8,
        Tempo:            1.5,
        KeyCardInPlay:    2.5);

    public static readonly ArchetypeWeights BorosEnergy = new(
        LifeDelta:        1.5,
        BoardPower:       1.5,
        BoardToughness:   1.0,
        OpponentThreats: -2.0,
        ManaSources:      1.5,
        HandSize:         2.0,
        Tempo:            1.5,
        KeyCardInPlay:    2.0);

    public static ArchetypeWeights ForArchetype(string name) => name switch
    {
        "Burn"         => Burn,
        "Prowess"      => Prowess,
        "BorosEnergy"  => BorosEnergy,
        _ => throw new ArgumentException($"Unknown archetype: {name}", nameof(name)),
    };
}
