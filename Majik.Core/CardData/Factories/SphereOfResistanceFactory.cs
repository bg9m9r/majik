using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sphere of Resistance
/// (Urza's Saga / Masques block — Artifact {2}).
///
/// Oracle text:
///   "Spells cost {1} more to cast."
///
/// ## Implementation
///
/// ### "Spells cost {1} more to cast." (CR 117.7 / CR 601.2f)
/// Wired as a <see cref="SpellCostIncreaseAbility"/> on the card. Predicate
/// matches every spell ("Spells …" with no qualifier — "each spell" by CR
/// reading); the per-cast delta is a flat {1} generic. Symmetric — applies
/// to both players' spells, identical shape to
/// <see cref="ThaliaGuardianOfThrabenFactory"/>'s noncreature-spell rider but
/// with no type predicate. <see cref="CostReduction.GetEffectiveCost(ICard,
/// Player, IEnumerable{Player}?)"/> scans every player's battlefield for
/// these riders, so opposing copies of Sphere of Resistance also tax the
/// caster.
///
/// ## Deferred
/// - LTB unregister: the <see cref="SpellCostIncreaseAbility"/> on the card
///   becomes inert when Sphere of Resistance is off the battlefield (the
///   <see cref="CostReduction.GetEffectiveCost"/> scanner only walks
///   battlefield permanents), so the cost rider lifts automatically without
///   an explicit unregister step.
/// - <see cref="CostReduction.GetEffectiveCost"/> call sites
///   (<see cref="Majik.Core.Game.SpellCastFlow"/>,
///   <see cref="Majik.Core.Game.TurnDriver"/>,
///   <see cref="Majik.Core.Players.Agents.HeuristicBotAgent"/>) currently
///   call the two-arg overload; they need to forward the all-players list
///   for the cost rider to apply in live play. Same follow-up tracked for
///   Damping Sphere / Thalia.
/// </summary>
[CardName("Sphere of Resistance")]
public static class SphereOfResistanceFactory
{
    public const string CardName = "Sphere of Resistance";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Construct Sphere of Resistance with the correct card shape — an
    /// Artifact {2} with the spell cost-increase rider attached as static
    /// metadata. Suitable for shape / dispatcher tests and for production
    /// use (no live continuous-effects registration needed for the cost
    /// rider).
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 117.7 / CR 601.2f — "Spells cost {1} more to cast."
        // Flat +{1} generic per cast; predicate matches every spell.
        // Symmetric — taxes any caster's spells while Sphere of Resistance
        // is on the battlefield. CostReduction.GetEffectiveCost walks all
        // players' battlefields for SpellCostIncreaseAbility riders, so the
        // increase fires regardless of whose turn it is or which player is
        // casting.
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: _ => true,
            extraGeneric: (_, _) => 1,
            description: "Spells cost {1} more to cast."));

        return card;
    }
}
