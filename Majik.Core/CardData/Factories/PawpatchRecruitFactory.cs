using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Pawpatch Recruit (Bloomburrow, {G}).
///
/// Creature — Rabbit Warrior, 2/1. Oracle text (Scryfall, verified 2026-06-02):
///   "Offspring {2} (You may pay an additional {2} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    Trample
///    Whenever a creature you control becomes the target of a spell or ability
///    an opponent controls, put a +1/+1 counter on target creature you control
///    other than that creature."
///
/// ## Offspring {2} (CR 702.169)
///
/// Wired through the generic Offspring keyword subsystem:
/// <see cref="OffspringAdditionalCost"/> (the optional additional cast cost,
/// CR 702.169a — drains {2} and stamps <see cref="Card.WasOffspringPaid"/>) +
/// <see cref="OffspringAbility.Attach"/> (the ETB trigger, CR 702.169b — when
/// this creature enters, if its Offspring cost was paid, create a 1/1 token
/// copy of it). The caller layers <see cref="BuildOffspringCost"/> onto the
/// cast via <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c>
/// when the caster chooses to pay; declining omits it.
///
/// ## Trample (CR 702.19)
///
/// A plain <see cref="KeywordAbility"/> marker — combat math reads it via the
/// keyword scan (same posture as every other Trample creature).
///
/// ## Deferred (v1 gap)
///
/// - <b>"Whenever a creature you control becomes the target of a spell or
///   ability an opponent controls, …"</b> — NOT wired. There is no
///   "becomes the target" event in the engine's catalog (CR 603.2 / 603.10 —
///   the engine publishes <c>SpellCastEvent</c> / ability-activation events but
///   no per-object "this permanent was targeted by an opponent's spell/ability"
///   notification), so this triggered ability has no event seam to bind to.
///   This is an isolated, net-new trigger surface independent of the Offspring
///   keyword subsystem this card unblocks; tracked as a per-card v1 gap here
///   (the established in-code soft-deferral home). The Offspring + Trample
///   halves are fully implemented.
/// </summary>
[CardName("Pawpatch Recruit")]
public static class PawpatchRecruitFactory
{
    public const string CardName = "Pawpatch Recruit";
    public const string PrintedManaCost = "{G}";
    public const string OffspringCostText = "{2}";

    /// <summary>CR 702.169 — the Offspring additional cost ({2}).</summary>
    public static ManaCost OffspringCost => ManaCost.Parse(OffspringCostText);

    /// <summary>Shape-only construction (no live trigger-manager wiring).</summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Pawpatch Recruit. When <paramref name="triggers"/> is supplied
    /// the Offspring ETB trigger is registered so the centralised event pump
    /// queues it automatically in a real match.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var rabbit = new Creature(
            CardName, PrintedManaCost, power: 2, toughness: 1,
            subtypes: new[] { CardSubtype.Rabbit, CardSubtype.Warrior })
        {
            Owner = owner,
            Controller = owner,
        };

        // Offspring {2} ETB token-copy (CR 702.169b).
        OffspringAbility.Attach(rabbit, triggers);

        // CR 702.169 — keyword marker (the "{cost}" rider rides on the
        // OffspringAdditionalCost the caller layers onto the cast).
        rabbit.AddAbility(new KeywordAbility("Offspring", rabbit, owner, arg: 2));

        // CR 702.19 — Trample.
        rabbit.AddAbility(new KeywordAbility("Trample", rabbit, owner));

        return rabbit;
    }

    /// <summary>Build the Offspring {2} additional cost for this spell.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);
}
