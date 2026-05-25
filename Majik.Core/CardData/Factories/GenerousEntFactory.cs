using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Generous Ent (The Lord of the Rings: Tales of
/// Middle-earth, {5}{G}).
///
/// Creature — Treefolk 5/7. Oracle text (Scryfall):
///   "Reach
///    When this creature enters, target player gains 4 life.
///    Forestcycling {G} ({G}, Discard this card: Search your library for
///    a Forest card, reveal it, put it into your hand, then shuffle.)"
///
/// ## Implemented (v1)
/// - <b>Creature — Treefolk {5}{G} 5/7</b>. Introduces
///   <see cref="CardSubtype.Treefolk"/>.
/// - <b>Reach</b> (CR 702.17) — <see cref="KeywordAbility"/> marker
///   consumed by <see cref="Majik.Core.Combat.CombatAbilities.HasReach"/>.
/// - <b>ETB target-player-gains-4-life trigger</b> (CR 603.6a): wired
///   via <see cref="Triggers.OnEnterBattlefieldSelf"/> with one 1..1
///   "target player" <see cref="TargetRequest"/> (<see cref="BotIntent.Heal"/>).
///   On resolution reads <see cref="TriggeredAbility.ChosenTargets"/>[0][0],
///   gates on the picked object still being a <see cref="Player"/> (CR
///   608.2b — illegal target → no-op), and calls
///   <see cref="Player.GainLife"/>(4). Self-target legal per the printed
///   "target player" wording (no opponent gate).
/// - <b>Forestcycling {G}</b> (CR 702.32d) — routed through
///   <see cref="TypedCyclingFactory.Build"/> with cycle cost
///   <see cref="ManaCostCost"/>("{G}") and predicate
///   <c>c =&gt; c.HasSubtype(CardSubtype.Forest)</c>. The primitive
///   attaches the <see cref="ActivatedAbility"/> + a
///   <see cref="KeywordAbility"/>("Forestcycling") typed marker + a
///   "Cycling" generic marker (CR 702.32d typecycling IS Cycling),
///   layers <see cref="DiscardSelfCost"/> (CR 702.32a hand-zone gate)
///   on the cost stack, and on resolve tutors the first Forest card
///   from the controller's library to hand (agent prompt with
///   deterministic first-match fallback — CR 701.19a) + shuffles
///   (CR 701.20a) + publishes <see cref="CardCycledEvent"/> for the
///   CR 702.32d "Whenever a player cycles" subscribers (Lightning Rift,
///   Astral Slide, etc.).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. ETB trigger attached
///   for shape inspection; cycling ability attached with no event bus
///   (no <see cref="CardCycledEvent"/> publication).
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — fully
///   wired. ETB trigger registered for bus-driven firing; cycling
///   resolve publishes <see cref="CardCycledEvent"/> against the bus.
///
/// CR rule references: 205.3m (Treefolk subtype), 603.6a (ETB
/// triggered ability), 702.17 (Reach), 702.32 (Cycling),
/// 702.32d (typecycling).
/// </summary>
[CardName("Generous Ent")]
public static class GenerousEntFactory
{
    public const string CardName = "Generous Ent";
    public const string PrintedManaCost = "{5}{G}";
    public const int Power = 5;
    public const int Toughness = 7;
    public const string CyclingCost = "{G}";
    public const int LifeGainAmount = 4;

    /// <summary>
    /// Construct Generous Ent with no live wiring. ETB trigger attached
    /// for shape inspection; cycling ability attached without an event
    /// bus (shape-only — no <see cref="CardCycledEvent"/> publication).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Generous Ent. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered so a self-enter
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> queues the
    /// life-gain body. When <paramref name="eventBus"/> is supplied the
    /// cycling resolve body publishes <see cref="CardCycledEvent"/> so
    /// CR 702.32d "Whenever a player cycles" triggers fire.
    /// </summary>
    public static Creature Create(
        Player owner,
        TriggerManager? triggers,
        IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Treefolk });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.17 — Reach. KeywordAbility marker consumed by
        // CombatAbilities.HasReach (mirrors Kraul Harpooner / Endurance).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Reach", card, owner));

        // ----------------------------------------------------------------
        // ETB trigger (CR 603.6a):
        //   "When this creature enters, target player gains 4 life."
        // Single 1..1 "target player" TargetRequest. On resolve the
        // ChosenTargets[0][0] is read; if still a Player the engine
        // applies the lifegain. Self-target legal per the printed
        // "target player" wording (no opponent gate).
        // ----------------------------------------------------------------
        TriggeredAbility? etbTrigger = null;

        var etbEffect = new Effect(
            $"{CardName}: target player gains {LifeGainAmount} life",
            () =>
            {
                if (etbTrigger is null) return;
                if (etbTrigger.ChosenTargets.Count == 0
                    || etbTrigger.ChosenTargets[0].Count == 0)
                {
                    // CR 608.2b — no legal target, do nothing.
                    return;
                }

                if (etbTrigger.ChosenTargets[0][0] is not Player target)
                {
                    return;
                }

                target.GainLife(LifeGainAmount);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target player",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Heal),
            });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Forestcycling {G} — CR 702.32d. Routed through the shared
        // TypedCyclingFactory primitive with predicate
        //   c => c.HasSubtype(CardSubtype.Forest)
        // for the Forest-card tutor target. The primitive appends the
        // DiscardSelfCost hand-zone gate (CR 702.32a), attaches both the
        // "Forestcycling" typed keyword + the generic "Cycling" marker
        // (CR 702.32d — typecycling IS Cycling), and on resolve tutors
        // a Forest card via agent prompt with deterministic
        // first-match fallback (CR 701.19a) + shuffles (CR 701.20a) +
        // publishes CardCycledEvent (CR 702.32d).
        // ----------------------------------------------------------------
        TypedCyclingFactory.Build(
            source: card,
            cycleCost: new ManaCostCost(CyclingCost),
            predicate: c => c.HasSubtype(CardSubtype.Forest),
            typedKeyword: "Forestcycling",
            kindLabel: "Forest card",
            eventBus: eventBus);

        return card;
    }
}
