using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Scion of Draco (Modern Horizons 2, {10}).
///
/// Artifact Creature — Dragon 4/4. Oracle text (printed):
///   "Domain — This spell costs {2} less to cast for each basic land type
///    among lands you control.
///    Creatures you control of each creature type have first strike,
///    vigilance, trample, lifelink, and hexproof."
///
/// ## Implemented (v1)
/// - 4/4 Artifact Creature — Dragon, mana cost {10}. Multi-card-type via
///   <c>AddCardType(CardType.Artifact)</c> on a <see cref="Creature"/>
///   shell (CR 301.1 / 302.1 — same pattern as Wurmcoil Engine /
///   Walking Ballista).
/// - <b>Domain cost reduction (CR 702.16 / CR 117.7)</b>: at cast time
///   this spell costs {2} less per distinct basic land type
///   ({Plains, Island, Swamp, Mountain, Forest}) among lands the caster
///   controls. Reuses <see cref="TribalFlamesFactory.CountDomain"/> as
///   the canonical Domain counter (printed-subtypes mode — no live
///   <see cref="ContinuousEffectsService"/> here at cost-calculation
///   time; Tribal Flames itself runs in printed-subtypes mode through
///   the same dispatcher path). Floor-at-zero enforced by
///   <see cref="CostReduction.GetEffectiveCost"/>; coloured pips
///   untouched (Scion has none — its full cost is {10} generic).
///
/// ## Deferred (v1 gaps)
/// - <b>"Creatures you control of each creature type have first strike,
///   vigilance, trample, lifelink, and hexproof" keyword-grant rider.</b>
///   This needs a "for each creature type" Layer 6 grant scoped per
///   permanent on the controller's battlefield (each permanent gets the
///   keywords only if it shares a creature type with every other
///   creature the controller controls) — that machinery doesn't ship
///   here. Tier-2 follow-up. The cost-reduction is the headline.
/// </summary>
public static class ScionOfDracoFactory
{
    public const string CardName = "Scion of Draco";
    public const string PrintedManaCost = "{10}";
    public const int Power = 4;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Scion of Draco. The Domain cost reducer is attached as a
    /// <see cref="CostReductionAbility"/> using the whole-reducer shape:
    /// <c>reduction = 2 × CountDomain(caster)</c>.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Dragon });

        // CR 301.1 / 302.1 — Scion of Draco is an Artifact Creature. The
        // base Creature constructor only registers CardType.Creature;
        // additively flag the Artifact type for HasType lookups (mirrors
        // Wurmcoil Engine).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.16 (Domain) + CR 117.7 — "This spell costs {2} less to
        // cast for each basic land type among lands you control." Whole-
        // reducer shape: TribalFlamesFactory.CountDomain returns the
        // number of DISTINCT basic land types (max 5; Wastes excluded),
        // and we multiply by 2 here. Floor-at-zero is enforced by
        // CostReduction.GetEffectiveCost.
        card.AddAbility(new CostReductionAbility(
            totalReducer: caster => 2 * TribalFlamesFactory.CountDomain(caster, effects: null),
            description: "Domain — costs {2} less per basic land type you control"));

        return card;
    }
}
