using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Phoenix Chick (Dominaria United, {R}).
///
/// Creature — Phoenix 1/1. Oracle text (verified against Scryfall 2026-06-02):
///   "Flying, haste
///    This creature can't block.
///    Whenever you attack with three or more creatures, you may pay {R}{R}.
///    If you do, return this card from your graveyard to the battlefield
///    tapped and attacking with a +1/+1 counter on it."
///
/// Base shape (name, Creature, Phoenix, {R}, 1/1) is materialised from the
/// embedded JSON definition (<c>phoenix-chick.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The keyword markers, the
/// can't-block restriction, and the graveyard-recursion attack trigger are
/// layered on top here — none of those is expressible in the JSON
/// AbilityDefinition schema (same posture as
/// <see cref="LegionLoyalistFactory"/> / <see cref="KariZevSkyshipRaiderFactory"/>).
///
/// ## Implemented (v1)
/// - <b>Flying (CR 702.9) + Haste (CR 702.10)</b> — <see cref="KeywordAbility"/>
///   markers consumed by the combat / summoning-sickness subsystem.
/// - <b>"This creature can't block" (CR 509.1b)</b> — a non-expiring
///   <see cref="CombatRestrictionEffect"/> with
///   <see cref="CombatRestriction.CannotBlock"/> targeting the Chick itself,
///   registered on the card's <see cref="Creature.ActiveEffects"/> so the
///   combat validator (<c>CombatValidator.CanBlock</c>) rejects it as a
///   blocker. Static, so it does not expire at end of turn.
/// - <b>"Whenever you attack with three or more creatures …" (CR 508.1f)</b> —
///   a <see cref="TriggeredAbility"/> over <see cref="AttackersDeclaredEvent"/>
///   that fires once per declare-attackers step when (a) the Chick's
///   controller/owner is the attacking player and (b) three or more creatures
///   are attacking. Unlike Battalion (<see cref="LegionLoyalistFactory"/>) the
///   Chick is NOT required to be among the attackers — it lives in the
///   graveyard, so the trigger declares <c>activeZones = {Graveyard}</c>
///   (CR 603.6d — abilities that function only from a non-battlefield zone),
///   mirroring <see cref="ArclightPhoenixFactory"/>'s graveyard trigger.
/// - <b>Resolve body</b> — re-checks the Chick is still in its owner's
///   graveyard at resolution (CR 603.6d / CR 608.2), then moves it to the
///   battlefield, stamps a <see cref="CounterType.PlusOnePlusOne"/> counter
///   (CR 614 — "with a +1/+1 counter on it" is part of the same event so the
///   counter is placed as it enters), and splices it into the in-progress
///   combat tapped and attacking the current defender via
///   <see cref="CombatManager.AddTappedAndAttackingToken"/> (CR 508.3 — enters
///   tapped; CR 508.4 — attacking). Same combat-splice path Kari Zev uses for
///   Ragavan; here the returned card is a real card rather than a token.
///
/// ## Deferred (v1 gaps)
/// - <b>"you may pay {R}{R}"</b> — the optional mana cost is auto-paid at v1
///   (same simplification as <see cref="ArclightPhoenixFactory"/>'s auto-accept
///   of its "you may return"). The whole ability still no-ops when fewer than
///   three creatures attack. A real pay-or-decline prompt arrives with the
///   agent-mana-payment surface.
/// - <b>No-combat fallback</b> — when <paramref name="combat"/> is null
///   (dispatcher / shape tests) the Chick still returns to the battlefield
///   with its counter, just untapped and not attacking (the "tapped and
///   attacking" fidelity requires a live combat to splice into). Same posture
///   as <see cref="KariZevSkyshipRaiderFactory"/>.
/// </summary>
[CardName("Phoenix Chick")]
public static class PhoenixChickFactory
{
    public const string CardName = "Phoenix Chick";
    public const string Slug = "phoenix-chick";

    /// <summary>CR 508.1f — three or more attacking creatures.</summary>
    public const int MinAttackers = 3;

    /// <summary>
    /// Construct Phoenix Chick with no live runtime wiring. The keyword
    /// markers, can't-block restriction, and the attack trigger are attached
    /// to the card shape; the trigger is not registered with a
    /// <see cref="TriggerManager"/> and the return body uses the no-combat
    /// fallback. This is the overload <see cref="NamedCardFactory"/> dispatches
    /// to.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, combat: null);

    /// <summary>
    /// Construct Phoenix Chick with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the graveyard attack trigger is
    /// registered so an <see cref="AttackersDeclaredEvent"/> for the owner's
    /// 3+-creature attack lands it on the stack automatically.</param>
    /// <param name="combat">When supplied, the returned Chick is spliced into
    /// the in-progress combat tapped and attacking
    /// (<see cref="CombatManager.AddTappedAndAttackingToken"/>).</param>
    public static Creature Create(Player owner, TriggerManager? triggers, CombatManager? combat)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Phoenix, {R}, 1/1). The JSON carries no abilities — everything
        // behavioural is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.9 — Flying. CR 702.10 — Haste. KeywordAbility markers.
        card.AddAbility(new KeywordAbility("Flying", card, owner));
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // CR 509.1b — "This creature can't block." A non-expiring CannotBlock
        // restriction scoped to the Chick itself, consulted by
        // CombatValidator.CanBlock via the card's ContinuousEffectsService.
        card.ActiveEffects ??= new ContinuousEffectsService();
        card.ActiveEffects.Register(
            new CombatRestrictionEffect(
                CombatRestriction.CannotBlock, card, expiresAtEndOfTurn: false));

        // CR 508.1f / CR 603.6d — "Whenever you attack with three or more
        // creatures, you may pay {R}{R}. If you do, return this card from your
        // graveyard to the battlefield tapped and attacking with a +1/+1
        // counter on it." Graveyard-resident attack-declared trigger.
        var returnEffect = new Effect(
            $"{CardName}: return from graveyard tapped & attacking with a +1/+1 counter (CR 508.1f)",
            () => ResolveReturn(card, owner, combat));

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<AttackersDeclaredEvent>(
                (e, _) => IsAttackMatch(e, card, owner)),
            effects: new IEffect[] { returnEffect },
            activeZones: new[] { ZoneType.Graveyard });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }

    // --- Trigger condition (CR 508.1f) -----------------------------------

    /// <summary>
    /// CR 508.1f — "Whenever you attack with three or more creatures."
    /// Keys on the Chick's controller (the owner while it sits in the
    /// graveyard) being the attacking player and at least three creatures
    /// being declared as attackers. The Chick itself is in the graveyard, so
    /// — unlike Battalion — it is NOT required to be among the attackers.
    /// </summary>
    private static bool IsAttackMatch(AttackersDeclaredEvent e, Creature card, Player owner)
    {
        var controller = card.Controller ?? owner;

        // CR 109.5 — "you attack" keys on the controller being the attacking
        // player.
        if (!ReferenceEquals(e.Combat.AttackingPlayer, controller)) return false;

        var total = 0;
        foreach (var atk in e.Combat.Attackers)
        {
            if (atk?.Creature != null) total++;
        }

        return total >= MinAttackers;
    }

    // --- Resolution body (CR 508.3 / CR 614 / CR 608.2) ------------------

    /// <summary>
    /// Return the Chick from its owner's graveyard to the battlefield tapped
    /// and attacking with a +1/+1 counter. Re-checks the graveyard residency
    /// at resolution (CR 603.6d / CR 608.2) and no-ops if the Chick has moved.
    /// The "you may pay {R}{R}" cost is auto-paid at v1 (see class remarks).
    /// </summary>
    private static void ResolveReturn(Creature card, Player owner, CombatManager? combat)
    {
        var controller = card.Controller ?? owner;

        // CR 603.6d / CR 608.2 — re-check the Chick is still in the graveyard
        // it would return from. If it has moved, the return no-ops.
        if (card.Zone != ZoneType.Graveyard) return;
        if (!owner.Zones.Graveyard.GetCards().Contains(card)) return;

        // Move graveyard → battlefield (the card's owner owns the graveyard;
        // it enters under its controller).
        owner.Zones.Graveyard.RemoveCard(card);
        controller.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);
        card.SetController(controller);

        // CR 614 — "with a +1/+1 counter on it." Placed as it enters.
        card.Counters.Add(CounterType.PlusOnePlusOne, 1);

        // CR 508.3 — enters tapped; CR 508.4 — attacking the current defender.
        // No-combat fallback: when no combat is live the Chick stays on the
        // battlefield untapped and not attacking.
        combat?.AddTappedAndAttackingToken(card);
    }
}
