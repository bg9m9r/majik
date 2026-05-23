using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Aether Spellbomb (Mirrodin / reprints).
///
/// Artifact — {1}. Oracle text:
///   "{U}, Sacrifice this artifact: Return target creature to its owner's hand.
///    {1}, Sacrifice this artifact: Draw a card."
///
/// ## Implemented (v1)
/// - Card identity (Artifact, mana cost {1}, owner / controller wiring).
/// - <b>{U}, Sacrifice: bounce target creature</b> — wired as an
///   <see cref="ActivatedAbility"/> with a <see cref="ManaCostCost"/>("{U}")
///   plus <see cref="AdditionalCost"/>.Sacrifice on the spellbomb itself.
///   A single <see cref="TargetRequest"/> is declared so the activating
///   player's agent picks a creature target at activation (Rule 602.2b).
///   The resolution effect reads <see cref="ActivatedAbility.ChosenTargets"/>
///   and bounces the chosen creature to its owner's hand. The sacrifice is
///   carried out by the effect closure (mirrors Mishra's Bauble — the
///   generic <see cref="AdditionalCost.Pay"/> sacrifice path is a stub).
/// - <b>{1}, Sacrifice: draw a card</b> — second
///   <see cref="ActivatedAbility"/> on the same card. <see cref="ManaCostCost"/>("{1}")
///   plus self-sacrifice; resolution moves the spellbomb to its owner's
///   graveyard and draws one card for the controller.
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter targets to "creature only" — resolution-time guard handles
///   illegal targets (CR 608.2b — effect involving an illegal target does
///   nothing).
/// - <b>ZoneService routing for the bounce</b>: mirrors Karakas /
///   Teferi, Time Raveler — raw zone manipulation, so leave-the-battlefield
///   triggers via <see cref="Majik.Core.Events.CardMovedEvent"/> are not
///   emitted by this path. Wire ZoneService through when the broader
///   bounce-pipeline pass lands.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is currently a no-op
///   stub. The effect closure performs the zone move so behavior is
///   observable. Remove the explicit move-to-graveyard once
///   <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// </summary>
public static class AetherSpellbombFactory
{
    public const string CardName = "Aether Spellbomb";

    /// <summary>
    /// Construct Aether Spellbomb owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Artifact Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var spellbomb = new Artifact(CardName, "{1}");
        spellbomb.SetOwner(owner);
        spellbomb.SetController(owner);

        // ----------------------------------------------------------------
        // {U}, Sacrifice this artifact: Return target creature to its
        // owner's hand. CR 602 — activated ability with a single target
        // request. The resolution effect reads ChosenTargets and gates on
        // the creature type at resolution (CR 608.2b — illegal target →
        // effect does nothing).
        // ----------------------------------------------------------------
        ActivatedAbility? bounceAbility = null;
        var bounceEffect = new Effect(
            "Aether Spellbomb: return target creature to owner's hand + sac self",
            () =>
            {
                if (bounceAbility != null
                    && bounceAbility.ChosenTargets.Count > 0
                    && bounceAbility.ChosenTargets[0].Count > 0
                    && bounceAbility.ChosenTargets[0][0] is Creature creature
                    && creature.Owner != null)
                {
                    BounceToOwnersHand(creature);
                }

                SacrificeSelf(spellbomb, owner);
            });

        bounceAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{U}"),
                AdditionalCost.Sacrifice(spellbomb),
            },
            effects: new IEffect[] { bounceEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });

        spellbomb.AddAbility(bounceAbility);

        // ----------------------------------------------------------------
        // {1}, Sacrifice this artifact: Draw a card.
        // ----------------------------------------------------------------
        var drawEffect = new Effect(
            "Aether Spellbomb: draw a card + sac self",
            () =>
            {
                SacrificeSelf(spellbomb, owner);

                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return; // empty-library loss handled by SBAs
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var drawAbility = new ActivatedAbility(
            source: spellbomb,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
                AdditionalCost.Sacrifice(spellbomb),
            },
            effects: new IEffect[] { drawEffect });

        spellbomb.AddAbility(drawAbility);

        return spellbomb;
    }

    /// <summary>
    /// Move <paramref name="creature"/> from its current zone to its
    /// owner's hand. Mirrors the bounce primitive used by Karakas and
    /// Teferi, Time Raveler.
    /// </summary>
    private static void BounceToOwnersHand(Creature creature)
    {
        var ownerOfCreature = creature.Owner;
        if (ownerOfCreature == null) return;

        var holder = creature.Controller ?? ownerOfCreature;
        switch (creature.Zone)
        {
            case ZoneType.Battlefield:
                holder.Zones.Battlefield.RemoveCard(creature);
                break;
            case ZoneType.Graveyard:
                ownerOfCreature.Zones.Graveyard.RemoveCard(creature);
                break;
            case ZoneType.Exile:
                ownerOfCreature.Zones.Exile.RemoveCard(creature);
                break;
        }

        ownerOfCreature.Zones.Hand.AddCard(creature);
        creature.SetZone(ZoneType.Hand);
    }

    /// <summary>
    /// Move the spellbomb from the battlefield to its owner's graveyard.
    /// Defensive against double-execution (idempotent if already
    /// sacrificed). Mirrors the Mishra's Bauble sacrifice closure — the
    /// generic <see cref="AdditionalCost.Pay"/> sacrifice path is a
    /// stub.
    /// </summary>
    private static void SacrificeSelf(Artifact spellbomb, Player owner)
    {
        if (spellbomb.Zone != ZoneType.Battlefield) return;
        owner.Zones.Battlefield.RemoveCard(spellbomb);
        owner.Zones.Graveyard.AddCard(spellbomb);
        spellbomb.SetZone(ZoneType.Graveyard);
    }
}
