using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Oliphaunt (Tarkir: Dragonstorm, {5}{R}).
///
/// Creature — Elephant 6/4. Oracle text:
///   "Trample
///    Whenever this creature attacks, another target creature you control
///    gets +2/+0 and gains trample until end of turn.
///    Mountaincycling {1} ({1}, Discard this card: Search your library for
///    a Mountain card, reveal it, put it into your hand, then shuffle.)"
///
/// ## Implemented (v1)
///
/// - <b>6/4 Creature — Elephant {5}{R}</b>, mana value 6. Red from the {R}
///   pip (CR 105.2). Owner / controller stamped.
///
/// - <b>Trample (CR 702.19)</b> — wired as a <see cref="KeywordAbility"/>
///   marker; read by <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>
///   in the combat damage assignment path.
///
/// - <b>Attack trigger (CR 508.1f / CR 603.1)</b> — "Whenever Oliphaunt
///   attacks, another target creature you control gets +2/+0 and gains
///   trample until end of turn." Wired via <see cref="Triggers.OnAttackSelf"/>
///   on <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>. The
///   effect reads <see cref="TriggeredAbility.ChosenTargets"/>[0][0]; guards
///   for (a) the target being a live <see cref="Creature"/> on the battlefield
///   (CR 608.2b), and (b) the target NOT being Oliphaunt itself ("another").
///   On a legal target:
///   - Registers <see cref="PumpUntilEndOfTurnEffect"/>(+2, 0) on the
///     target's <see cref="Creature.ActiveEffects"/> (CR 613.1g Layer 7c).
///   - Registers <see cref="GrantKeywordUntilEndOfTurnEffect"/>("Trample") on
///     the same service (CR 613.1c Layer 6 / CR 514.2).
///   When <see cref="Creature.ActiveEffects"/> is null (shape-only test path),
///   both registrations are no-ops.
///
/// - <b>Mountaincycling {1} (CR 702.29 / 702.32d)</b> — routed through
///   <see cref="TypedCyclingFactory.Build"/> with cycle cost
///   <see cref="ManaCostCost"/>("{1}") and predicate
///   <c>c =&gt; c.HasSubtype(CardSubtype.Mountain)</c>. The primitive attaches
///   the <see cref="ActivatedAbility"/> + a "Mountaincycling" typed keyword
///   marker + a "Cycling" generic marker (CR 702.32d — typecycling IS
///   Cycling), layers <see cref="DiscardSelfCost"/> (CR 702.32a hand-zone
///   gate), and on resolve tutors the first Mountain card from the
///   controller's library to hand (CR 701.19a) + shuffles (CR 701.20a) +
///   publishes <see cref="CardCycledEvent"/> (when an event bus is supplied).
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. Attack trigger and
///   mountaincycling ability are attached; trigger is NOT registered with
///   any <see cref="TriggerManager"/> (no event bus). Suitable for
///   dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. The attack trigger is registered with
///   <paramref name="triggers"/> so <c>CreatureAttacksEvent</c> from
///   Oliphaunt lands it on the stack automatically.
///   <see cref="CardCycledEvent"/> is published on cycling resolve when
///   <paramref name="eventBus"/> is supplied.
///
/// CR rule references: 105.2 (color from pips), 202.3 (mana value),
/// 508.1f (attack trigger timing), 603.1 (triggered ability), 608.2b
/// (illegal-target no-op), 613.1c (Layer 6 ability grant), 613.1g
/// (Layer 7c P/T modification), 701.19a (library search), 701.20a
/// (shuffle), 702.19 (Trample), 702.29 (Mountaincycling), 702.32
/// (Cycling), 702.32d (typecycling).
/// </summary>
[CardName("Oliphaunt")]
public static class OliphantFactory
{
    public const string CardName = "Oliphaunt";
    public const string PrintedManaCost = "{5}{R}";
    public const int Power = 6;
    public const int Toughness = 4;
    public const string CyclingCost = "{1}";

    /// <summary>Power boost applied by the attack trigger.</summary>
    public const int AttackPumpPower = 2;

    /// <summary>Toughness boost applied by the attack trigger (zero).</summary>
    public const int AttackPumpToughness = 0;

    /// <summary>
    /// Construct Oliphaunt with no event bus or trigger manager.
    /// The attack trigger is attached for shape; it is NOT registered
    /// with any <see cref="TriggerManager"/>. Suitable for dispatcher /
    /// shape tests and the <see cref="NamedCardFactory"/> dispatcher path.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Oliphaunt with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied the mountaincycling resolve
    /// body publishes <see cref="CardCycledEvent"/> so CR 702.32d
    /// "Whenever a player cycles a card" triggers fire. Also required by
    /// <paramref name="triggers"/> for the attack-trigger subscription.</param>
    /// <param name="triggers">When supplied, the attack trigger is
    /// registered so a <see cref="Majik.Core.Domain.DomainEvents.CreatureAttacksEvent"/>
    /// from Oliphaunt automatically queues the ability.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elephant });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Trample — CR 702.19. KeywordAbility marker read by
        // CombatAbilities.HasTrample in the combat damage assignment path.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / CR 603.1.
        //   "Whenever Oliphaunt attacks, another target creature you
        //    control gets +2/+0 and gains trample until end of turn."
        // Fires on CreatureAttacksEvent matching this card.
        // The effect reads ChosenTargets[0][0]:
        //   - no-ops if the chosen target is not a Creature on the
        //     battlefield (CR 608.2b illegal-target fizzle);
        //   - no-ops if the target is Oliphaunt itself ("another" rider);
        //   - otherwise registers PumpUntilEndOfTurnEffect(+2, 0) +
        //     GrantKeywordUntilEndOfTurnEffect("Trample") on the target's
        //     ActiveEffects (null → no-op, shape-only path).
        // ----------------------------------------------------------------
        TriggeredAbility? attackTrigger = null;

        var attackEffect = new Effect(
            $"{CardName}: attack trigger — another target creature you control gets +2/+0 and gains trample until end of turn",
            () =>
            {
                if (attackTrigger == null) return;
                var chosen = attackTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                var raw = chosen[0][0];

                // CR 608.2b — target must still be a Creature on the battlefield.
                if (raw is not Creature target) return;
                if (target.Zone != ZoneType.Battlefield) return;

                // "Another" — Oliphaunt cannot target itself.
                if (ReferenceEquals(target, card)) return;

                // Shape-only path: if the target has no ActiveEffects service,
                // both registrations are no-ops (same posture as InfuriateFactory /
                // TemurBattleRageFactory).
                if (target.ActiveEffects == null) return;

                // CR 613.1g Layer 7c — +2/+0 until end of turn.
                target.ActiveEffects.Register(
                    new PumpUntilEndOfTurnEffect(target, AttackPumpPower, AttackPumpToughness));

                // CR 613.1c Layer 6 / CR 702.19 — grant Trample until end of turn.
                target.ActiveEffects.Register(
                    new GrantKeywordUntilEndOfTurnEffect(target, "Trample"));
            });

        attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "another target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.CombatTrick,
                    // Live candidate gatherer — another creature the controller
                    // controls on the battlefield (not Oliphaunt itself, CR 109.5
                    // "another").
                    CandidateGatherer: ctx => (card.Controller ?? owner).Zones.Battlefield
                        .GetCards()
                        .OfType<Creature>()
                        .Where(c => !ReferenceEquals(c, card))
                        .Cast<object>()
                        .ToList()),
            });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // Mountaincycling {1} — CR 702.29 / 702.32d. Routed through the
        // shared TypedCyclingFactory primitive with predicate
        //   c => c.HasSubtype(CardSubtype.Mountain)
        // for the Mountain-card tutor target. The primitive attaches both
        // the "Mountaincycling" typed keyword and the generic "Cycling"
        // marker (CR 702.32d), appends DiscardSelfCost (hand-zone gate,
        // CR 702.32a), and on resolve tutors a Mountain card via
        // agent-driven pick / deterministic first-match fallback (CR
        // 701.19a) + shuffles (CR 701.20a) + publishes CardCycledEvent
        // (CR 702.32d) when an event bus is supplied.
        // ----------------------------------------------------------------
        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: c => c.HasSubtype(CardSubtype.Mountain),
            typedKeyword: "Mountaincycling",
            kindLabel: "Mountain card",
            eventBus: eventBus);

        return card;
    }
}
