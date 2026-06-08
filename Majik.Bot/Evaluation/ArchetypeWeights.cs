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
    double KeyCardInPlay,
    /// <summary>
    /// Weight for the lethal-proximity closing term in
    /// <see cref="BoardEval"/>. Controls how strongly the eval rewards
    /// driving the opponent's life toward zero. The term is non-linear:
    /// marginal damage value rises steeply as opp life drops below 5 (see
    /// <see cref="BoardEval.LethalProximityBonus"/>). Aggressive archetypes
    /// (Burn) weight this higher than controlling ones (BorosEnergy).
    /// </summary>
    double LethalProximity = 1.0)
{
    public static readonly ArchetypeWeights Burn = new(
        LifeDelta:        3.0,
        BoardPower:       1.5,
        BoardToughness:   0.2,
        OpponentThreats: -1.0,
        ManaSources:      0.8,  // a land on board > a land in hand for an aggressive deck
        HandSize:         0.3,
        Tempo:            1.0,
        KeyCardInPlay:    2.0,
        LethalProximity:  3.0); // burn races hard — every point closer to 0 is precious

    public static readonly ArchetypeWeights Prowess = new(
        LifeDelta:        1.0,
        BoardPower:       2.0,
        BoardToughness:   0.5,
        OpponentThreats: -2.0,
        ManaSources:      1.0,
        HandSize:         0.8,
        Tempo:            1.5,
        KeyCardInPlay:    2.5,
        LethalProximity:  2.5); // prowess converts board advantage to kills

    public static readonly ArchetypeWeights BorosEnergy = new(
        LifeDelta:        1.5,
        BoardPower:       1.5,
        BoardToughness:   1.0,
        OpponentThreats: -2.0,
        ManaSources:      1.5,
        HandSize:         2.0,
        Tempo:            1.5,
        KeyCardInPlay:    2.0,
        LethalProximity:  2.0); // midrange — still wants to close games

    /// <summary>
    /// Neutral midrange baseline for any archetype that does not (yet) have a
    /// hand-tuned weight table. Balanced across board / life / tempo so an
    /// untuned bot plays competently rather than crashing.
    ///
    /// <para>Every name in <see cref="Decks.BotDeckCatalog.Archetypes"/> is
    /// surfaced to the create-match bot picker (<c>GET /matches/archetypes</c>)
    /// and accepted by <c>MatchService</c>, so <see cref="ForArchetype"/> MUST
    /// return a usable table for all of them — throwing would turn a selectable
    /// bot into a match-creation crash. Bot-vs-bot smoke
    /// (<c>BotDeck_MirrorMatch_PlaysGame_NoCrash</c>) guards this for the whole
    /// catalog.</para>
    /// </summary>
    public static readonly ArchetypeWeights Default = new(
        LifeDelta:        1.5,
        BoardPower:       1.5,
        BoardToughness:   0.75,
        OpponentThreats: -1.5,
        ManaSources:      1.2,  // mana sources on board outvalue cards in hand
        HandSize:         0.8,
        Tempo:            1.0,
        KeyCardInPlay:    2.0,
        LethalProximity:  1.5); // sensible default: reward closing games

    /// <summary>Resolve the eval weights for an archetype, falling back to
    /// <see cref="Default"/> for any archetype without a bespoke table. Never
    /// throws — see <see cref="Default"/>.</summary>
    public static ArchetypeWeights ForArchetype(string name) => name switch
    {
        "Burn"         => Burn,
        "Prowess"      => Prowess,
        "BorosEnergy"  => BorosEnergy,
        _ => Default,
    };
}
