namespace Majik.Bot.Evaluation;

/// <summary>
/// Per-archetype eval weights. See spec section 5 for the table. Each
/// weight is a multiplier on the matching BoardEval component.
///
/// Burn = race plan (LifeDelta dominant). Prowess = tempo (BoardPower
/// dominant). BorosEnergy = midrange (HandSize + Life dominant).
/// AzoriusControl = attrition (CardAdvantage + PlaneswalkerEngine dominant).
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
    double LethalProximity = 1.0,
    /// <summary>
    /// Weight for the card-advantage differential term in
    /// <see cref="BoardEval"/>: <c>(self cards in hand − opponent cards in
    /// hand)</c>. A positive differential signals the bot is ahead on
    /// resources — the core attrition signal for control/midrange archetypes.
    ///
    /// <para>Control/midrange weight this HIGH (they win by out-resourcing
    /// opponents). Aggro weight it LOW — an aggro deck is often empty-handed
    /// by design.</para>
    ///
    /// <para>Note: <see cref="HandSize"/> is the bot's own hand count (absolute
    /// value) while <see cref="CardAdvantage"/> weights the <em>differential</em>
    /// (relative lead). Both coexist — HandSize encourages building hand size
    /// in general; CardAdvantage specifically rewards being ahead of the
    /// opponent. For archetypes that rely on running out their hand quickly
    /// (Burn, Prowess), keep this near zero to avoid penalising the intended
    /// play pattern.</para>
    /// </summary>
    double CardAdvantage = 0.0,
    /// <summary>
    /// Weight for the planeswalker-engine bonus in <see cref="BoardEval"/>.
    /// Each planeswalker the bot controls contributes a bonus scaled by its
    /// current loyalty: <c>loyalty × PlaneswalkerEngine</c>. Loyalty is a
    /// proxy for accumulated value — a high-loyalty Teferi has already
    /// generated several card-draws or bounces and threatens to keep doing so.
    ///
    /// <para>Control archetypes weight this HIGH — planeswalkers are their
    /// primary inevitability engine. Aggro archetypes weight it LOW because
    /// they rarely cast planeswalkers and the term would just add noise.</para>
    /// </summary>
    double PlaneswalkerEngine = 0.0,
    /// <summary>
    /// Weight for the deck-strategy advisory term in <see cref="BoardEval"/>.
    /// Folds in <see cref="Majik.Bot.Strategies.IDeckStrategy.StrategicScore"/>
    /// when a strategy is wired up. Without a strategy (<c>deck == null</c>) this
    /// term is zero and the eval is identical to before.
    ///
    /// <para>Default 1.0 — strategy scores are already calibrated by the
    /// implementing strategy; this weight lets a tuned profile dial the advisory
    /// bonus up or down relative to the existing eval terms.</para>
    /// </summary>
    double Strategic = 1.0)
{
    public static readonly ArchetypeWeights Burn = new(
        LifeDelta:           3.0,
        BoardPower:          1.5,
        BoardToughness:      0.2,
        OpponentThreats:    -1.0,
        ManaSources:         0.8,  // a land on board > a land in hand for an aggressive deck
        HandSize:            0.3,
        Tempo:               1.0,
        KeyCardInPlay:       2.0,
        LethalProximity:     3.0,  // burn races hard — every point closer to 0 is precious
        CardAdvantage:       0.1,  // aggro: nearly indifferent to card parity
        PlaneswalkerEngine:  0.0); // burn never runs walkers

    public static readonly ArchetypeWeights Prowess = new(
        LifeDelta:           1.0,
        BoardPower:          2.0,
        BoardToughness:      0.5,
        OpponentThreats:    -2.0,
        ManaSources:         1.0,
        HandSize:            0.8,
        Tempo:               1.5,
        KeyCardInPlay:       2.5,
        LethalProximity:     2.5,  // prowess converts board advantage to kills
        CardAdvantage:       0.2,  // tempo deck: card parity matters a little but not the focus
        PlaneswalkerEngine:  0.0); // prowess is spell-based, not walker-based

    public static readonly ArchetypeWeights BorosEnergy = new(
        LifeDelta:           1.5,
        BoardPower:          1.5,
        BoardToughness:      1.0,
        OpponentThreats:    -2.0,
        ManaSources:         1.5,
        HandSize:            2.0,
        Tempo:               1.5,
        KeyCardInPlay:       2.0,
        LethalProximity:     2.0,  // midrange — still wants to close games
        CardAdvantage:       1.0,  // midrange: card parity matters moderately
        PlaneswalkerEngine:  0.3); // some walkers in the sideboard / flex slots

    /// <summary>
    /// Azorius Control weights — attrition and inevitability. The plan is
    /// out-resource the opponent with counterspells, wraths, and Teferi;
    /// win on card advantage even when the life totals are close.
    ///
    /// <para>CardAdvantage is weighted HIGH: being up two cards in hand
    /// dominates a one-point life or board advantage. PlaneswalkerEngine is
    /// also HIGH because Teferi (Hero + Time Raveler) generates repeated
    /// value per loyalty counter. LethalProximity is kept low relative to
    /// aggro — control does not race; it accrues until it locks.</para>
    /// </summary>
    public static readonly ArchetypeWeights AzoriusControl = new(
        LifeDelta:           1.0,   // life matters less when you have counterspells
        BoardPower:          0.5,   // control wins on resources, not board width
        BoardToughness:      0.5,
        OpponentThreats:    -2.5,  // clearing opp threats is the whole game plan
        ManaSources:         1.5,   // mana is critical for holding up counters
        HandSize:            1.5,   // absolute hand size still matters
        Tempo:               1.2,   // holding up mana = tempo advantage
        KeyCardInPlay:       1.5,   // Solitude / Subtlety on board is impactful
        LethalProximity:     0.8,   // control doesn't race — but closing is still good
        CardAdvantage:       3.0,   // THE key signal: being up cards = winning at control
        PlaneswalkerEngine:  1.5);  // Teferi loyalty = accumulated card advantage

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
        LifeDelta:           1.5,
        BoardPower:          1.5,
        BoardToughness:      0.75,
        OpponentThreats:    -1.5,
        ManaSources:         1.2,  // mana sources on board outvalue cards in hand
        HandSize:            0.8,
        Tempo:               1.0,
        KeyCardInPlay:       2.0,
        LethalProximity:     1.5,  // sensible default: reward closing games
        CardAdvantage:       0.5,  // moderate: card parity is a weak positive signal
        PlaneswalkerEngine:  0.2); // small bonus for any walkers on board

    /// <summary>Resolve the eval weights for an archetype, falling back to
    /// <see cref="Default"/> for any archetype without a bespoke table. Never
    /// throws — see <see cref="Default"/>.</summary>
    public static ArchetypeWeights ForArchetype(string name) => name switch
    {
        "Burn"           => Burn,
        "Prowess"        => Prowess,
        "BorosEnergy"    => BorosEnergy,
        "AzoriusControl" => AzoriusControl,
        _ => Default,
    };
}
