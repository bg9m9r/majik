using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Thundertrap Trainer (Duskmourn, {1}{U}).
///
/// Creature — Otter Wizard, 1/2. Oracle text (Scryfall, verified 2026-06-12):
///   "Offspring {4} (You may pay an additional {4} as you cast this spell. If
///    you do, when this creature enters, create a 1/1 token copy of it.)
///    When this creature enters, look at the top four cards of your library.
///    You may reveal a noncreature, nonland card from among them and put it
///    into your hand. Put the rest on the bottom of your library in a random
///    order."
///
/// ## Offspring {4} (CR 702.169)
///
/// Wired through the generic Offspring keyword subsystem, mirroring
/// <see cref="ManifoldMouseFactory"/> / <see cref="PawpatchRecruitFactory"/>:
/// <see cref="OffspringAdditionalCost"/> (the optional additional cast cost,
/// CR 702.169a — drains {4} and stamps <see cref="Card.WasOffspringPaid"/>) +
/// <see cref="OffspringAbility.Attach"/> (the ETB trigger, CR 702.169b — when
/// this creature enters, if its Offspring cost was paid, create a 1/1 token
/// copy of it). The caller layers <see cref="BuildOffspringCost"/> onto the cast
/// via <see cref="Majik.Core.Game.SpellCastFlow"/>'s <c>additionalCosts</c> when
/// the caster chooses to pay; declining simply omits it.
///
/// ## ETB dig (CR 603.6a)
///
/// "When this creature enters, look at the top four cards of your library. You
/// may reveal a noncreature, nonland card from among them and put it into your
/// hand. Put the rest on the bottom of your library in a random order." Built
/// as a self-ETB <see cref="TriggeredAbility"/>
/// (<see cref="Triggers.OnEnterBattlefieldSelf"/>) whose resolution effect
/// mirrors <see cref="AncientStirringsFactory"/>'s peek/partition/bottom shape:
/// peek the top four, take the FIRST card that is neither a creature nor a land
/// (CR 105 type test) to hand, then re-bottom the rest in a shuffled (random,
/// CR 701.20a) order. Like Ancient Stirrings, the "may" opt-out and the
/// agent-driven choice of WHICH eligible card to reveal are v1 deferrals: the
/// default selector always reveals the first eligible card. Both halves attach
/// to the card via <see cref="Permanent.AddAbility"/> so the centralised ETB
/// event pump queues them in a real match; the optional
/// <paramref name="triggers"/> registration mirrors the Offspring analogues for
/// direct-call / shape tests.
/// </summary>
[CardName("Thundertrap Trainer")]
public static class ThundertrapTrainerFactory
{
    public const string CardName = "Thundertrap Trainer";
    public const string PrintedManaCost = "{1}{U}";
    public const string OffspringCostText = "{4}";

    /// <summary>CR 702.169 — the Offspring additional cost ({4}).</summary>
    public static ManaCost OffspringCost => ManaCost.Parse(OffspringCostText);

    /// <summary>Shape-only construction (no live trigger-manager wiring).</summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Thundertrap Trainer. When <paramref name="triggers"/> is
    /// supplied the Offspring ETB trigger and the dig ETB trigger are registered
    /// so the centralised event pump queues them automatically in a real match.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var otter = new Creature(
            CardName, PrintedManaCost, power: 1, toughness: 2,
            subtypes: new[] { CardSubtype.Otter, CardSubtype.Wizard })
        {
            Owner = owner,
            Controller = owner,
        };

        // Offspring {4} ETB token-copy (CR 702.169b).
        OffspringAbility.Attach(otter, triggers);

        // CR 702.169 — keyword marker (the "{cost}" rider rides on the
        // OffspringAdditionalCost the caller layers onto the cast).
        otter.AddAbility(new KeywordAbility("Offspring", otter, owner, arg: 4));

        // ETB dig (CR 603.6a).
        AttachEtbDig(otter, owner, triggers);

        return otter;
    }

    /// <summary>Build the Offspring {4} additional cost for this spell. Layer it
    /// onto the cast via SpellCastFlow's <c>additionalCosts</c> when the caster
    /// chooses to pay Offspring; omit it to decline.</summary>
    public static IAdditionalCost BuildOffspringCost(ICard card) =>
        new OffspringAdditionalCost(card, OffspringCost);

    /// <summary>
    /// "look at the top four cards of your library. You may reveal a
    /// noncreature, nonland card from among them and put it into your hand. Put
    /// the rest on the bottom of your library in a random order." (CR 603.6a /
    /// 701.20a). Mirrors <see cref="AncientStirringsFactory"/>'s peek / partition
    /// / shuffled-bottom shape with a noncreature-nonland filter.
    /// </summary>
    private static void AttachEtbDig(Creature otter, Player owner, TriggerManager? triggers)
    {
        var digEffect = new Effect(
            $"{CardName}: look at the top four, may reveal a noncreature/nonland card to hand, rest to the bottom in a random order",
            () =>
            {
                var controller = otter.Controller ?? owner;
                var lib = controller.Zones.Library;
                var peeked = lib.GetCards().Take(4).ToList();
                if (peeked.Count == 0) return;

                // "a noncreature, nonland card" — first eligible among the top
                // four (CR 105 type test). The "may" opt-out + agent choice of
                // WHICH eligible card is a v1 deferral (same posture as
                // AncientStirringsFactory): default to revealing the first.
                ICard? toHand = null;
                foreach (var c in peeked)
                {
                    if (c.HasType(CardType.Creature)) continue;
                    if (c.HasType(CardType.Land)) continue;
                    toHand = c;
                    break;
                }

                if (toHand != null)
                {
                    lib.RemoveCard(toHand);
                    controller.Zones.Hand.AddCard(toHand);
                    toHand.SetZone(ZoneType.Hand);
                }

                // "Put the rest on the bottom of your library in a random
                // order." (CR 701.20a). Remove the remaining peeked cards then
                // re-append them (Zone.AddCard appends to the bottom) in a
                // shuffled order.
                var rest = peeked.Where(c => !ReferenceEquals(c, toHand)).ToList();
                Shuffle(rest);
                foreach (var c in rest) lib.RemoveCard(c);
                foreach (var c in rest)
                {
                    lib.AddCard(c);
                    c.SetZone(ZoneType.Library);
                }
            });

        var trigger = new TriggeredAbility(
            source: otter,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(otter),
            effects: new IEffect[] { digEffect },
            activeZones: new[] { ZoneType.Battlefield });

        otter.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    private static void Shuffle<T>(IList<T> list)
    {
        // Fisher-Yates via Random.Shared (same posture as AncientStirringsFactory).
        var rng = System.Random.Shared;
        for (var i = list.Count - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
