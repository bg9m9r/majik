using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aura of Silence (Tempest, {1}{W}{W}).
///
/// Enchantment — {1}{W}{W}. Oracle text:
///   "Artifact and enchantment spells your opponents cast cost {2} more
///    to cast."
///   "Sacrifice Aura of Silence: Destroy target artifact or enchantment."
///
/// ## Implemented (v1)
///
/// ### Card identity
/// Plain Enchantment (not an Aura subtype in the engine's current shape —
/// Aura of Silence's "Aura" is on the card's printed type line but the
/// card itself is a global enchantment, not an attached Aura — it has
/// no enchant target). Cost {1}{W}{W}, owner / controller wired.
///
/// ### "Artifact and enchantment spells your opponents cast cost {2} more"
/// CR 117.7 / CR 601.2f. Wired via <see cref="SpellCostIncreaseAbility"/>
/// on the card. The increase function gates on caster identity — only
/// non-controller casters incur the {2} surcharge ("your opponents cast"),
/// so the controller's own artifact/enchantment spells pass through
/// unchanged. The card-type predicate selects artifact OR enchantment
/// spells.
///
/// <see cref="CostReduction.GetEffectiveCost(ICard, Player,
/// IEnumerable{Player}?)"/> walks every player's battlefield for
/// <see cref="SpellCostIncreaseAbility"/> riders, so a Bob-side Aura of
/// Silence taxes Alice's artifact/enchantment spells as soon as cost
/// calculation is asked for them with the all-players list threaded in.
///
/// ### "Sacrifice Aura of Silence: Destroy target artifact or enchantment"
/// CR 602 — activated ability. Cost = <see cref="AdditionalCost.Sacrifice"/>
/// on Aura of Silence itself; no mana component (pure sacrifice). A 1..1
/// <see cref="TargetRequest"/> for "target artifact or enchantment" is
/// declared so the activator picks a permanent at activation (CR 602.2b).
/// On resolution:
/// <list type="number">
///   <item>Sacrifice Aura of Silence (battlefield → owner's graveyard —
///     same closure shape as Caustic Caterpillar / Aether Spellbomb /
///     Mind Stone; the generic <see cref="AdditionalCost.Pay"/> sacrifice
///     path is a stub).</item>
///   <item>Target permanent is still on the battlefield (CR 608.2b).</item>
///   <item>Target is an artifact OR an enchantment.</item>
///   <item>If all pass: destroy via
///     <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///     <see cref="ZoneMoveReason.Destroy"/> (CR 701.7 — indestructible
///     cancels per CR 702.12, active regeneration shield consumed per
///     CR 701.15).</item>
///   <item>If any fails: the sacrifice still happens (cost was paid) and
///     the destroy is a clean no-op (CR 608.2b — illegal target →
///     effect does nothing).</item>
/// </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter activation targets to "artifact or enchantment" — resolution-
///   time guard handles illegal targets (CR 608.2b). Same posture as
///   Caustic Caterpillar / Assassin's Trophy.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move directly so behaviour
///   is observable — remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// - <b>Controller-change re-evaluation</b>: the cost rider captures the
///   Aura's controller at factory time. If Aura of Silence changes
///   controllers mid-game, the "your opponents" set still resolves
///   against <c>card.Controller</c> at evaluation time (the rider reads
///   the live property), so control changes are honoured automatically.
/// </summary>
[CardName("Aura of Silence")]
public static class AuraOfSilenceFactory
{
    public const string CardName = "Aura of Silence";
    public const string Cost = "{1}{W}{W}";

    /// <summary>
    /// Construct Aura of Silence owned and controlled by
    /// <paramref name="owner"/>. Both the cost-increase static and the
    /// sacrifice-self activated ability are attached structurally.
    /// </summary>
    public static Enchantment Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Artifact and enchantment spells your opponents cast cost {2}
        // more to cast." (CR 117.7 / CR 601.2f)
        //
        // Predicate: spell is an artifact OR enchantment. The caster gate
        // ("your opponents") lives in the extraGeneric function — we
        // return 0 when the caster is the same player who controls Aura
        // of Silence, +2 otherwise. card.Controller is read at evaluation
        // time, so controller-change effects (Switcheroo, Threads of
        // Disloyalty, …) are honoured without re-registration.
        // ----------------------------------------------------------------
        card.AddAbility(new SpellCostIncreaseAbility(
            predicate: c => c.HasType(CardType.Artifact)
                            || c.HasType(CardType.Enchantment),
            extraGeneric: (_, caster) =>
                ReferenceEquals(caster, card.Controller) ? 0 : 2,
            description: "Artifact and enchantment spells your opponents cast cost {2} more to cast."));

        // ----------------------------------------------------------------
        // Sacrifice Aura of Silence: Destroy target artifact or enchantment.
        // CR 602 — activated ability. Cost = AdditionalCost.Sacrifice on
        // Aura of Silence itself; no mana component (pure sacrifice).
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
    /// owner's graveyard. Idempotent. Mirrors the closure used by Caustic
    /// Caterpillar / Aether Spellbomb / Mind Stone — the generic
    /// <see cref="AdditionalCost.Pay"/> sacrifice path is a no-op stub, so
    /// the effect closure performs the zone move directly.
    /// </summary>
    private static void SacrificeSelf(Enchantment card, Player owner)
    {
        if (card.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(card);
        owner.Zones.Graveyard.AddCard(card);
        card.SetZone(ZoneType.Graveyard);
    }
}
