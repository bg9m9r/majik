using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Damping Sphere (Dominaria — Artifact {2}).
///
/// Oracle text:
///   "If a land is tapped for two or more mana, it produces {C} instead
///    of any other type and amount."
///   "Each spell a player casts costs {1} more to cast for each other
///    spell that player has cast this turn."
///
/// ## Implementation
///
/// ### Rider 1 — land-mana cap (CR 605, CR 106.6).
/// Wired via <see cref="DampingSphereCappedManaAbility"/> which
/// <see cref="EffectiveManaAbilities.For(Permanent, ContinuousEffectsService?,
/// Player?, IEnumerable{Player}?)"/> applies whenever any battlefield
/// contains a card named Damping Sphere. The wrapper invokes the inner
/// mana ability (so the land still taps + any "Pay 1 life" / painland
/// side-effects still fire), then replaces the produced mana with a
/// single {C} whenever the total would be two or more. Symmetric — caps
/// the controller's own lands too. Only mana abilities sourced from a
/// <see cref="Land"/> are wrapped (Mox / Lotus Petal / Sol Ring etc.
/// pass through unchanged, matching the printed "land" qualifier).
///
/// Callers wiring a real game loop must thread the all-players list into
/// <see cref="EffectiveManaAbilities.For"/> for the cap to fire;
/// <see cref="Majik.Core.Costs.ManaPaymentResolver"/> is not yet
/// updated to plumb this through automatically — listed as a deferred
/// gap. Tests exercise the cap by calling
/// <see cref="EffectiveManaAbilities.For"/> directly with the player
/// list.
///
/// ### Rider 2 — per-spell cost increase (CR 117.7, CR 601.2f).
/// Wired via a <see cref="SpellCostIncreaseAbility"/> on the card. The
/// per-cast delta is <c>TurnState.SpellsCastByPlayer(caster)</c> — i.e.
/// {0} for the caster's first spell of the turn, {1} for the second
/// ({1} more for the one "other" spell), {2} for the third, … Matching
/// the printed text. <see cref="CostReduction.GetEffectiveCost(ICard,
/// Player, IEnumerable{Player}?)"/> scans every player's battlefield
/// for the rider, so opposing copies of Damping Sphere still tax the
/// caster.
///
/// The increase consults the live <see cref="TurnState"/> handed in at
/// factory time; the <see cref="Create(Player)"/> overload that returns
/// a card-shape only (no TurnState) builds the rider against a
/// freshly-allocated TurnState that always returns zero — suitable for
/// shape/dispatch tests where cost calc isn't exercised. Production
/// wiring should use <see cref="Create(Player, TurnState)"/>.
///
/// ### Deferred
/// - <see cref="ManaPaymentResolver"/> doesn't yet forward an all-players
///   list to <see cref="EffectiveManaAbilities.For"/>; payment paths in
///   the real game loop currently bypass the cap. Surfacing the cap into
///   <see cref="ManaPaymentResolver"/> + <see cref="SpellCastFlow"/> is a
///   follow-up — the helpers + tests here lock in the semantics.
/// - <see cref="CostReduction.GetEffectiveCost"/> call sites
///   (<see cref="SpellCastFlow"/>, <see cref="Majik.Core.Game.TurnDriver"/>,
///   <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>) currently
///   call the two-arg overload; they need to forward the all-players list
///   for the per-spell cost rider to apply in live play. Same follow-up.
/// </summary>
[CardName("Damping Sphere")]
public static class DampingSphereFactory
{
    public const string CardName = "Damping Sphere";
    public const string Cost = "{2}";

    /// <summary>
    /// Construct Damping Sphere with the correct card shape and both
    /// printed riders attached as static metadata. The per-spell cost
    /// rider uses a fresh <see cref="TurnState"/> that reports zero
    /// spells cast — suitable for shape / dispatch tests. Production
    /// wiring should call <see cref="Create(Player, TurnState)"/> so the
    /// rider reads the live per-turn tally.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, turnState: null);

    /// <summary>
    /// Construct Damping Sphere with both printed riders attached. The
    /// per-spell cost rider reads <paramref name="turnState"/> for the
    /// caster's cast count at evaluation time; pass the same TurnState
    /// instance the game's <see cref="Majik.Core.Game.TurnDriver"/>
    /// owns.
    /// </summary>
    public static Artifact Create(Player owner, TurnState? turnState)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // Rider 2 — "Each spell a player casts costs {1} more to cast for
        // each other spell that player has cast this turn." The cost rider
        // applies to every spell ("each spell a player casts"); the per-
        // cast delta is the count of OTHER spells that caster has cast
        // earlier this turn. CostReduction.GetEffectiveCost runs BEFORE
        // TurnState records the cast on the cast bus, so the live count
        // already excludes the spell being announced — no off-by-one
        // correction needed.
        var liveTurnState = turnState;
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: _ => true,
            extraGeneric: (_, caster) =>
                liveTurnState?.SpellsCastByPlayer(caster) ?? 0,
            description: "Each spell a player casts costs {1} more to cast for each other spell that player has cast this turn."));

        return card;
    }
}
