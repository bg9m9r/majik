using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Caustic Caterpillar (Magic Origins, {G}).
///
/// Creature — Insect 1/1. Oracle text:
///   "Sacrifice this creature: Destroy target artifact or enchantment."
///
/// ## Implemented (v1)
/// - Card identity: Creature — Insect, mana cost {G}, P/T 1/1, owner / controller.
/// - <b>Sacrifice self: Destroy target artifact or enchantment</b> — single
///   <see cref="ActivatedAbility"/> with <see cref="AdditionalCost.Sacrifice"/>
///   on the caterpillar itself (no mana component — the cost is pure
///   sacrifice). A 1..1 <see cref="TargetRequest"/> for "target artifact or
///   enchantment" is declared so the activating agent picks a permanent at
///   activation (CR 602.2b). On resolution:
///   <list type="number">
///     <item>Sacrifice the caterpillar (battlefield → owner's graveyard —
///       same closure shape as Aether Spellbomb / Mind Stone / Expedition
///       Map, the generic <see cref="AdditionalCost.Pay"/> sacrifice path
///       is a stub).</item>
///     <item>Target permanent is still on the battlefield (CR 608.2b).</item>
///     <item>Target is an artifact OR an enchantment (the printed
///       "artifact or enchantment" predicate).</item>
///     <item>If both pass: destroy via
///       <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///       <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///       cancels per CR 702.12, active regeneration shield consumed per
///       CR 701.15).</item>
///     <item>If any fails: the sacrifice still happens (cost was paid) and
///       the destroy is a clean no-op (CR 608.2b — illegal target →
///       effect does nothing).</item>
///   </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter activation targets to "artifact or enchantment" — resolution-
///   time guard handles illegal targets (CR 608.2b). Same posture as
///   Aether Spellbomb / Assassin's Trophy.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// - <b>BotIntent on activation target</b>: the <see cref="TargetRequest"/>
///   carries <see cref="BotIntent.Removal"/> so the bot's target picker
///   ranks artifact / enchantment removal correctly; agent-side activation
///   prompting still relies on the generic ActivatedAbility surface.
/// </summary>
[CardName("Caustic Caterpillar")]
public static class CausticCaterpillarFactory
{
    public const string CardName = "Caustic Caterpillar";
    public const string PrintedManaCost = "{G}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Caustic Caterpillar owned and controlled by
    /// <paramref name="owner"/>. The single "sacrifice: destroy target
    /// artifact or enchantment" activated ability is attached structurally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Insect });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Sacrifice this creature: Destroy target artifact or enchantment.
        // CR 602 — activated ability. Cost = AdditionalCost.Sacrifice on
        // the caterpillar itself; no mana component (pure sacrifice).
        // CR 608.2b — resolution-time guard ensures the chosen target is
        // still a legal artifact / enchantment on the battlefield.
        // CR 701.7 — destroy via MoveToGraveyard(Destroy).
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice self + destroy target artifact/enchantment",
            () =>
            {
                SacrificeSelf(card, owner);

                if (sacAbility == null
                    || sacAbility.ChosenTargets.Count == 0
                    || sacAbility.ChosenTargets[0].Count == 0)
                {
                    return;
                }

                if (sacAbility.ChosenTargets[0][0] is not Permanent target) return;

                // CR 608.2b — illegal-target check at resolution.
                if (target.Zone != ZoneType.Battlefield) return;
                if (!target.HasType(CardType.Artifact)
                    && !target.HasType(CardType.Enchantment))
                {
                    return;
                }

                // CR 701.7 — destroy. Indestructible (CR 702.12) cancels;
                // active regeneration shield (CR 701.15) is consumed.
                OracleSpellBinder.MoveToGraveyard(target, ZoneMoveReason.Destroy);
            });

        sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Sacrifice(card),
            },
            effects: new IEffect[] { sacEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            });

        card.AddAbility(sacAbility);

        return card;
    }

    /// <summary>
    /// CR 701.16 — move <paramref name="card"/> from the battlefield to its
    /// owner's graveyard. Idempotent. Mirrors the closure used by Aether
    /// Spellbomb / Mind Stone / Expedition Map — the generic
    /// <see cref="AdditionalCost.Pay"/> sacrifice path is a no-op stub, so
    /// the effect closure performs the zone move directly.
    /// </summary>
    private static void SacrificeSelf(Creature card, Player owner)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
