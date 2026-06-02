using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cindervines (Modern Horizons, {R}{G}).
///
/// Enchantment. Oracle text:
///   "Whenever an opponent casts a noncreature spell, this enchantment
///    deals 1 damage to that player."
///   "{1}, Sacrifice this enchantment: Destroy target artifact or
///    enchantment. This enchantment deals 2 damage to that permanent's
///    controller."
///
/// ## Implemented (v1)
///
/// ### Card identity
/// Plain Enchantment at {R}{G}, owner / controller wired.
///
/// ### "Whenever an opponent casts a noncreature spell, deal 1 to that
/// player" (CR 603.1)
/// <see cref="EventTriggerCondition{TEvent}"/> over
/// <see cref="SpellCastEvent"/> — same shape as
/// <see cref="KambalConsulOfAllocationFactory"/>:
///   * CR 109.5 — the spell's controller is not Cindervines' controller
///     ("an opponent"). The controller's own casts do not fire.
///   * CR 202.3 — the spell's card is not a <see cref="CardType.Creature"/>
///     (printed-type check; same posture as the Kambal / Eidolon
///     noncreature-spell trigger family).
/// On resolution the caster takes 1 damage via <see cref="Fx.DealDamage"/>
/// (CR 119 — routes a Player target through <see cref="Player.LoseLife"/>,
/// so <see cref="Player.LifeLostThisTurn"/> ticks). The pending caster is
/// boxed in a single-element array so the resolve body re-reads the right
/// player (Kambal / Eidolon-style closure).
///
/// ### "{1}, Sacrifice this enchantment: Destroy target artifact or
/// enchantment. Deal 2 to that permanent's controller." (CR 602)
/// Activated ability. Costs = {1} mana (<see cref="ManaCostCost"/>) +
/// <see cref="AdditionalCost.Sacrifice"/> on Cindervines itself. A 1..1
/// <see cref="TargetRequest"/> for "target artifact or enchantment" is
/// declared (CR 602.2b). On resolution (mirrors
/// <see cref="AuraOfSilenceFactory"/>):
/// <list type="number">
///   <item>Sacrifice Cindervines (battlefield → owner's graveyard — the
///     generic <see cref="AdditionalCost.Pay"/> sacrifice path is a stub,
///     so the closure performs the move directly, same as Aura of
///     Silence / Caustic Caterpillar).</item>
///   <item>CR 608.2b — the chosen target must still be a battlefield
///     artifact OR enchantment; otherwise the destroy + damage are a clean
///     no-op (the sacrifice cost was still paid).</item>
///   <item>CR 701.7 — destroy via <see cref="Fx.MoveToGraveyard"/> with
///     <see cref="ZoneMoveReason.Destroy"/> (indestructible CR 702.12 /
///     regeneration CR 701.15 honoured by the binder gate).</item>
///   <item>CR 119 — deal 2 damage to the destroyed permanent's controller.
///     The controller is captured BEFORE the destroy so a permanent that
///     changes zone (and clears its controller) still routes the damage to
///     the player who controlled it as the ability resolved.</item>
/// </list>
///
/// ## Deferred (v1 gaps)
/// - <b>Target legality in ActionValidator</b>: the validator does not yet
///   filter activation targets to "artifact or enchantment" — the
///   resolution-time guard handles illegal targets (CR 608.2b). Same
///   posture as Aura of Silence / Caustic Caterpillar.
/// - <b>Sacrifice payment side effects</b>: the engine's generic
///   <see cref="AdditionalCost"/> sacrifice payment is a no-op stub; the
///   closure performs the zone move directly. Remove the explicit move
///   once <see cref="AdditionalCost.Pay"/> performs the sacrifice itself.
/// - <b>Spell-type check for split / DFC / Adventure casts</b>: the
///   noncreature gate reads <see cref="ICard.HasType"/> on the cast card's
///   printed type set — the same shape gap noted on Kambal / Eidolon.
/// </summary>
[CardName("Cindervines")]
public static class CindervinesFactory
{
    public const string CardName = "Cindervines";
    public const string Cost = "{R}{G}";

    /// <summary>
    /// Construct Cindervines with no live <see cref="TriggerManager"/>
    /// wiring. The trigger is attached to the card shape so dispatcher
    /// tests see it; pass the (owner, triggers) overload to register it
    /// for live <see cref="SpellCastEvent"/> dispatch.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Cindervines with optional <see cref="TriggerManager"/>
    /// wiring. When <paramref name="triggers"/> is supplied, the
    /// opponent-noncreature-cast trigger is registered so any qualifying
    /// <see cref="SpellCastEvent"/> automatically queues the 1-damage
    /// effect.
    /// </summary>
    public static Enchantment Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // "Whenever an opponent casts a noncreature spell, this enchantment
        //  deals 1 damage to that player." (CR 603.1)
        // ----------------------------------------------------------------
        var pendingCaster = new Player?[] { null };

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            if (caster is null) return false;

            // CR 109.5 — "an opponent" reads against the trigger's
            // controller. Cindervines' own casts do not fire.
            if (ReferenceEquals(caster, card.Controller)) return false;

            // CR 202.3 — noncreature spell (printed-type check on the
            // spell's source card; same posture as Kambal / Eidolon).
            if (e.Spell.Card.HasType(CardType.Creature)) return false;

            pendingCaster[0] = caster;
            return true;
        });

        var damageEffect = new Effect(
            $"{CardName}: deal 1 to that opponent",
            () =>
            {
                var caster = pendingCaster[0];
                pendingCaster[0] = null;
                if (caster is null) return;

                // CR 119 — damage to a player. Fx.DealDamage routes a
                // Player target through Player.LoseLife (LifeLostThisTurn
                // ticks).
                Fx.DealDamage(caster, 1);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { damageEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // ----------------------------------------------------------------
        // "{1}, Sacrifice this enchantment: Destroy target artifact or
        //  enchantment. This enchantment deals 2 damage to that
        //  permanent's controller." (CR 602)
        // ----------------------------------------------------------------
        ActivatedAbility? sacAbility = null;
        var sacEffect = new Effect(
            $"{CardName}: sacrifice self + destroy target artifact/enchantment + 2 to its controller",
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

                // CR 119 — "that permanent's controller". Captured BEFORE
                // the destroy: MoveToGraveyard clears the permanent's
                // controller, so reading it afterwards would lose the
                // recipient.
                var controllerToBurn = target.Controller;

                // CR 701.7 — destroy. Indestructible (CR 702.12) /
                // regeneration (CR 701.15) honoured by the binder gate.
                Fx.MoveToGraveyard(target, ZoneMoveReason.Destroy);

                if (controllerToBurn is not null)
                {
                    Fx.DealDamage(controllerToBurn, 2);
                }
            });

        sacAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}"),
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
    /// owner's graveyard. Idempotent. Mirrors the closure used by Aura of
    /// Silence / Caustic Caterpillar — the generic
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
