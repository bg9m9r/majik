using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Umezawa's Jitte (Betrayers of Kamigawa, {2}).
///
/// Legendary Artifact — Equipment. Oracle text:
///   "Whenever equipped creature deals combat damage, put two charge
///    counters on Umezawa's Jitte."
///   "Remove a charge counter from Umezawa's Jitte: Choose one —
///     • Umezawa's Jitte deals 2 damage to any target.
///     • Target creature gets -1/-1 until end of turn.
///     • You gain 2 life."
///   "Equip {2}."
///
/// ## Implementation
///
/// - <b>Combat-damage trigger (CR 510, CR 603.1)</b>: fires whenever the
///   equipped creature deals combat damage (to any target — creature,
///   planeswalker, or player). The trigger condition matches
///   <see cref="CombatDamageDealtEvent"/>.<see cref="CombatDamageDealtEvent.Source"/>
///   against the source's current <see cref="Permanent.AttachedTo"/>.
///   On resolution, two <see cref="CounterType.Charge"/> counters are
///   added to Jitte.
/// - <b>Three modal activated abilities (CR 602.1, CR 700.2)</b>: each
///   shares the same activation cost (Remove a charge counter from
///   Jitte, via <see cref="RemoveChargeCounterCost"/>) but encodes one
///   of the three printed modes as a separate
///   <see cref="ActivatedAbility"/>. The activating player picks which
///   mode to fire by choosing which ability to activate — the engine
///   does not yet model modal activated abilities natively (the
///   <see cref="Majik.Core.Game.SpellDefinition"/> mode infrastructure
///   is spell-side only). Fanning out per-mode preserves "choose one"
///   semantics + cost-once semantics correctly: each activation pays
///   one charge counter, picks one mode.
///     1. <b>2 damage to any target</b> — single 1..1 "any target"
///        TargetRequest; on resolution
///        <see cref="OracleSpellBinder.DealDamage"/> is invoked.
///     2. <b>-1/-1 until end of turn to target creature</b> — single
///        1..1 "target creature" TargetRequest; on resolution a
///        <see cref="PumpUntilEndOfTurnEffect"/>(-1, -1) is registered
///        against the supplied <see cref="ContinuousEffectsService"/>.
///        When no effects service is wired (shape-only path) the -1/-1
///        is a no-op.
///     3. <b>You gain 2 life</b> — no target; resolution calls
///        <see cref="Player.GainLife"/>.
/// - <b>Equip {2}</b> — activated ability (CR 702.6). Cost is <c>{2}</c>.
///   v1 picker is deterministic: the first creature on the controller's
///   battlefield. Same shape as
///   <see cref="ColossusHammerFactory"/> / <see cref="SkullclampFactory"/>.
///
/// ## Lifecycle
///
/// The combat-damage trigger gates on the current
/// <see cref="Permanent.AttachedTo"/> at fire time, so re-equipping
/// shifts which creature's combat damage feeds the trigger. The trigger
/// fires on ANY combat damage by the equipped creature (creature,
/// planeswalker, or player target) — the oracle text deliberately omits
/// "to a player", which is the most-asked Jitte rules question.
///
/// The single-arg <see cref="Create(Player)"/> overload omits service
/// wiring and produces the correct card shape only — suitable for
/// factory-shape / dispatch tests. The combat trigger is attached for
/// shape but not registered, and the -1/-1 modal effect is a no-op.
///
/// ## Deferred
///
/// - <b>Attach-target prompt</b> for Equip — v1 picks first creature
///   deterministically.
/// - <b>Native modal activated abilities</b> — the engine routes mode
///   choice through three separate activated abilities. The agent picks
///   which to activate; there is no single "Jitte ability" with a
///   <c>ChooseModeAsync</c> call. When modal-activated infra ships, the
///   three abilities can collapse to one.
/// </summary>
[CardName("Umezawa's Jitte")]
public static class UmezawasJitteFactory
{
    public const string CardName = "Umezawa's Jitte";
    public const string Cost = "{2}";
    public const string EquipCost = "{2}";

    /// <summary>
    /// Constructs Umezawa's Jitte with no live runtime wiring (the shape /
    /// dispatcher path). The combat-damage trigger is attached but not
    /// registered with a <see cref="TriggerManager"/>; the -1/-1 modal
    /// activated ability is a no-op (no <see cref="ContinuousEffectsService"/>
    /// to register the effect against).
    /// </summary>
    public static Artifact Create(Player owner)
        => Create(owner, continuousEffects: null, triggers: null);

    /// <summary>
    /// Constructs Umezawa's Jitte with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the combat-damage trigger
    /// is registered so a <see cref="CombatDamageDealtEvent"/> from the
    /// equipped creature automatically queues the ability. When
    /// <paramref name="continuousEffects"/> is supplied the -1/-1 modal
    /// activated ability registers a <see cref="PumpUntilEndOfTurnEffect"/>
    /// against it at resolution.
    /// </summary>
    public static Artifact Create(
        Player owner,
        ContinuousEffectsService? continuousEffects,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(
            name: CardName,
            manaCost: Cost,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Equipment });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------
        // Combat-damage trigger — CR 510 / CR 603.1.
        //   "Whenever equipped creature deals combat damage, put two
        //    charge counters on Umezawa's Jitte."
        // Matches any CombatDamageDealtEvent whose Source is the
        // currently-equipped creature (Jitte's AttachedTo). Fires on
        // damage to any target — creature, planeswalker, or player.
        // --------------------------------------------------------------
        var combatEffect = new Effect(
            $"{CardName}: put two charge counters on Jitte",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                card.Counters.Add(CounterType.Charge, 2);
            });

        var combatTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                var equipped = card.AttachedTo;
                if (equipped == null) return false;
                return ReferenceEquals(e.Source, equipped);
            }),
            effects: new IEffect[] { combatEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(combatTrigger);
        triggers?.RegisterTriggeredAbility(combatTrigger);

        // --------------------------------------------------------------
        // Modal activated ability — Mode 1: "Jitte deals 2 damage to
        //   any target." (CR 602.1, CR 700.2.)
        // Cost: Remove a charge counter from Jitte.
        // Target: 1..1 "any target".
        // --------------------------------------------------------------
        ActivatedAbility? damageAbility = null;
        var damageEffect = new Effect(
            $"{CardName}: deal 2 damage to any target",
            () =>
            {
                if (damageAbility == null) return;
                if (damageAbility.ChosenTargets.Count == 0) return;
                if (damageAbility.ChosenTargets[0].Count == 0) return;
                var target = damageAbility.ChosenTargets[0][0];
                OracleSpellBinder.DealDamage(target, 2);
            });
        damageAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new RemoveChargeCounterCost(card) },
            effects: new IEffect[] { damageEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });
        card.AddAbility(damageAbility);

        // --------------------------------------------------------------
        // Modal activated ability — Mode 2: "Target creature gets
        //   -1/-1 until end of turn." (CR 602.1, CR 700.2, CR 613.)
        // Cost: Remove a charge counter from Jitte.
        // Target: 1..1 "target creature".
        // Effect registers a Layer 7c PumpUntilEndOfTurnEffect against
        // the supplied ContinuousEffectsService; without one the effect
        // is a no-op (shape-only path).
        // --------------------------------------------------------------
        ActivatedAbility? minusAbility = null;
        var minusEffect = new Effect(
            $"{CardName}: target creature gets -1/-1 until end of turn",
            () =>
            {
                if (minusAbility == null) return;
                if (minusAbility.ChosenTargets.Count == 0) return;
                if (minusAbility.ChosenTargets[0].Count == 0) return;
                if (minusAbility.ChosenTargets[0][0] is not Creature creature) return;
                if (continuousEffects == null) return;
                continuousEffects.Register(new PumpUntilEndOfTurnEffect(creature, -1, -1));
            });
        minusAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new RemoveChargeCounterCost(card) },
            effects: new IEffect[] { minusEffect },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            });
        card.AddAbility(minusAbility);

        // --------------------------------------------------------------
        // Modal activated ability — Mode 3: "You gain 2 life."
        // Cost: Remove a charge counter from Jitte.
        // No target.
        // --------------------------------------------------------------
        var lifeEffect = new Effect(
            $"{CardName}: you gain 2 life",
            () => owner.GainLife(2));
        var lifeAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new RemoveChargeCounterCost(card) },
            effects: new IEffect[] { lifeEffect });
        card.AddAbility(lifeAbility);

        // --------------------------------------------------------------
        // Equip {2} — activated ability (CR 702.6).
        //   "{2}: Attach to target creature you control. Activate only
        //    as a sorcery."
        // v1 picker: deterministic first controller-side creature.
        // CR 117.1a / 307.5 sorcery-speed restriction enforced via
        // ActionValidator (sorcerySpeed: true below).
        // --------------------------------------------------------------
        var equipEffect = new Effect(
            $"{CardName}: equip — attach to a creature you control",
            () =>
            {
                var bearer = owner.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, owner));
                if (bearer == null) return;
                card.AttachTo(bearer);
            });
        var equipAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { new ManaCostCost(EquipCost) },
            effects: new IEffect[] { equipEffect },
            sorcerySpeed: true);
        card.AddAbility(equipAbility);

        return card;
    }
}
