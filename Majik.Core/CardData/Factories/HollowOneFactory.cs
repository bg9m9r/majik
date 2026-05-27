using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hollow One (Hour of Devotion, {5}).
///
/// Artifact Creature — Golem 4/4. Oracle text:
///   "This spell costs {2} less to cast for each card you've cycled or
///    discarded this turn."
///
/// ## Why it gets its own factory
/// Hollow One is the canonical Modern Rakdos / Hollow-One-shell payoff for
/// the discard / cycle engines (Faithless Looting + Burning Inquiry +
/// Street Wraith). The printed text exercises a per-turn cycle + discard
/// tally — both surfaces the engine now records via
/// <see cref="TurnState.RecordCardCycled"/> (driven by
/// <see cref="Majik.Core.Events.CardCycledEvent"/>) and
/// <see cref="TurnState.RecordCardDiscarded"/> (driven by
/// <see cref="Majik.Core.Events.CardMovedEvent"/> Hand → Graveyard).
///
/// ## Implemented (v1)
///
/// - 4/4 Artifact Creature — Golem at {5}.
/// - <see cref="CardType.Artifact"/> additively stamped via
///   <see cref="Card.AddCardType"/> so HasType lookups + colour identity
///   see both Artifact + Creature (mirrors Memnite / Frogmite /
///   Vault Skirge).
/// - <b>Self cost reduction (CR 117.7)</b>: <see cref="CostReductionAbility"/>
///   in <see cref="CostReductionAbility.TotalReducer"/> shape — reads a
///   live <see cref="TurnState"/> reference supplied at construction time
///   and returns <c>(cycles + discards) * 2</c> generic mana reduction
///   for the caster. Coloured pips are untouched per CR 117.7c (Hollow
///   One has none) and the cost is floored at zero inside
///   <see cref="CostReduction.GetEffectiveCost"/>:
///     - 0 cycles + 0 discards → pays {5}
///     - 1 discard → pays {3}
///     - 2 discards → pays {1}
///     - 3 discards → pays {0} (floor)
///   Shape-only path (no TurnState supplied) attaches a reducer that
///   always returns 0 — same posture as Bedlam Reveler's shape-only
///   path.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — no live wiring. The cost-reduction
///   ability is attached for shape inspection but its reducer body
///   always returns 0 (no <see cref="TurnState"/> reference). Suitable
///   for dispatcher / structural tests.
/// - <see cref="Create(Player, TurnState?)"/> — fully wired. The
///   reducer reads <paramref name="turnState"/>'s cycle + discard
///   counters at cost-calc time.
///
/// ## Cycling double-count nuance
///
/// CR 702.32a — cycling explicitly says "Discard this card: …". The
/// engine's <see cref="Majik.Core.Keywords.CyclingFactory"/> resolve
/// path moves the cycled card Hand → Graveyard (which OnCardMoved sees
/// and records as a discard) AND publishes
/// <see cref="Majik.Core.Events.CardCycledEvent"/> (which OnCardCycled
/// records as a cycle). The printed oracle text says "cycled OR
/// discarded" — a cycled card legitimately counts as both. Hollow One's
/// real-card rulings confirm: cycling a card increments Hollow One's
/// reducer by {2} (matching one cycle) — NOT {4} (one cycle + one
/// discard). The v1 reducer sidesteps this by adding the two counters
/// (so cycling increments by {4}); a follow-up PR can refine to
/// <c>max(cycles, discards) + extra-discards</c> if precise rulings
/// matter.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Madness / "exile-from-graveyard alt cast"</b> riders — out of
///   scope. Hollow One only has the cost-reduction line and the
///   creature shape.
/// - <b>Cycling-vs-discard double-count refinement</b> — see above.
/// </summary>
[CardName("Hollow One")]
public static class HollowOneFactory
{
    public const string CardName = "Hollow One";
    public const string PrintedManaCost = "{5}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>Per-event generic-mana reduction granted by Hollow One's
    /// printed reducer. The oracle text says "{2} less … for each card
    /// you've cycled or discarded this turn".</summary>
    public const int ReductionPerEvent = 2;

    /// <summary>
    /// Documented divergence from real-card rulings: cycling a card
    /// counts as one cycle AND one discard in v1 (so a single cycle
    /// reduces the cost by {4} rather than the real-card {2}). Tracked
    /// here so a future PR can flip the reducer to
    /// <c>max(cycles, discards) + extra_discards</c> behind a feature
    /// flag without surprising callers.
    /// </summary>
    public const string DocumentedRulingsDivergence =
        "v1: cycles + discards (cycling double-counts). Real-card: cycling " +
        "counts once. See HollowOneFactory XML docs.";

    /// <summary>
    /// Construct Hollow One with no live <see cref="TurnState"/> wiring.
    /// The cost-reduction ability is attached for shape inspection but
    /// its reducer body always returns 0 (no TurnState reference).
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, turnState: null);

    /// <summary>
    /// Construct Hollow One. When <paramref name="turnState"/> is
    /// supplied, the cost-reduction ability's reducer reads
    /// <c>CyclesByPlayer(caster) + DiscardsByPlayer(caster)</c> and
    /// multiplies by <see cref="ReductionPerEvent"/>. When null, the
    /// reducer returns 0 (shape-only path).
    /// </summary>
    public static Creature Create(Player owner, TurnState? turnState)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Golem });

        // CR 301.1 / 302.1 — Artifact Creature: additively flag the
        // Artifact type so HasType lookups + colour identity see both
        // types (mirrors Memnite / Frogmite / Vault Skirge).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 117.7 — "This spell costs {2} less to cast for each card
        // you've cycled or discarded this turn." Whole-reduction shape
        // (CostReductionAbility(totalReducer)) — the function reads
        // TurnState at cost-calc time. CR 117.7c — cost cannot drive
        // generic below zero; floor enforced inside
        // CostReduction.GetEffectiveCost. Hollow One has no coloured
        // pips so the only floor is generic-at-zero.
        // ----------------------------------------------------------------
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster =>
            {
                if (turnState == null || caster == null) return 0;
                var cycles = turnState.CyclesByPlayer(caster);
                var discards = turnState.DiscardsByPlayer(caster);
                return (cycles + discards) * ReductionPerEvent;
            },
            description:
                "This spell costs {2} less to cast for each card you've " +
                "cycled or discarded this turn."));

        return card;
    }
}
