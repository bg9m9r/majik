using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sengir Vampire (Alpha / reprints, {3}{B}{B}).
///
/// Creature — Vampire 4/4. Oracle text (Scryfall, verified):
///   "Flying (This creature can't be blocked except by creatures with
///    flying or reach.)
///    Whenever a creature dealt damage by this creature this turn dies,
///    put a +1/+1 counter on this creature."
///
/// The original mono-black bomb that grows every time one of its
/// victims dies — a 4/4 flier that snowballs into a game-ender after a
/// single profitable combat.
///
/// ## Shape source
/// Card identity (name, {3}{B}{B}, 4/4, Creature — Vampire) is loaded from
/// <c>Majik.Core/CardData/Cards/sengir-vampire.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. Flying and the damage-linked dies
/// trigger are attached in code below — the JSON ability schema does not yet
/// express keyword markers or a "creature this dealt damage to died" linkage.
///
/// ## Implemented (v1)
///
/// - <b>4/4 Creature — Vampire at {3}{B}{B}.</b>
///
/// - <b>Flying — keyword ability (CR 702.9).</b> Wired as a
///   <see cref="KeywordAbility"/> marker so combat code reads it the same way
///   it reads every other printed Flying creature (mirrors
///   <see cref="SpriteDragonFactory"/> / <see cref="HopeOfGhirapurFactory"/>);
///   <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> returns true.
///
/// - <b>Damage-linked dies trigger (CR 603.1 / 603.2e).</b>
///   "Whenever a creature dealt damage by this creature this turn dies, put a
///   +1/+1 counter on this creature." Implemented as a per-instance,
///   per-turn tracker over the event bus (same posture as
///   <see cref="HopeOfGhirapurFactory"/>'s damaged-players set, but keyed on
///   creature <see cref="ICard.InstanceId"/>):
///     1. A <see cref="DamageDealtEvent"/> handler stamps the
///        <c>InstanceId</c> of every creature Sengir deals damage to into a
///        per-turn set. The match is <c>Source == this card</c>, so both
///        combat damage AND non-combat damage count (CR 120 — the printed
///        condition is "dealt damage", not "dealt combat damage"). The base
///        <see cref="DamageDealtEvent"/> and its
///        <see cref="CombatDamageDealtEvent"/> subclass both flow through
///        this handler.
///     2. A <see cref="CardMovedEvent"/> handler watches for a creature
///        moving Battlefield → Graveyard (the engine's "dies" signal — CR
///        700.4) whose <c>InstanceId</c> is in the damaged-this-turn set; on
///        a match it puts one +1/+1 counter on Sengir Vampire via
///        <see cref="CountersService.Add"/> (CR 122.1 — routed through the
///        <see cref="ReplacementBus"/> so Hardened Scales / Doubling Season
///        can rewrite the count per CR 614, and publishing
///        <see cref="CounterAddedEvent"/> so downstream payoffs chain).
///        The victim's id is removed from the set after the counter is
///        placed so a re-cast token with the same id (extraordinarily rare)
///        cannot double-fire.
///     3. A <see cref="TurnStartedEvent"/> handler clears the set at the
///        start of every turn (printed "this turn" scope — CR 514.x).
///   The set is keyed on <c>InstanceId</c> rather than card references so it
///   survives the victim leaving the battlefield: the
///   <see cref="CardMovedEvent"/> fires as the creature transitions
///   Battlefield → Graveyard, and the id was recorded while it was still on
///   the battlefield taking damage.
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. Flying + the trigger
///   ability marker are attached for shape / dispatch tests; no event-bus
///   subscriptions, so the per-turn tracker is inert (no counters accrue).
/// - <see cref="Create(Player, IEventBus?, ReplacementBus?)"/> — fully
///   wired. The damage tracker, dies watcher, and turn-start clear all
///   subscribe to the supplied <see cref="IEventBus"/>; the counter
///   placement routes through the optional <see cref="ReplacementBus"/>.
/// </summary>
[CardName("Sengir Vampire")]
public static class SengirVampireFactory
{
    public const string CardName = "Sengir Vampire";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sengir-vampire");

    /// <summary>+1/+1 counters placed per dead victim (CR 122.1).</summary>
    public const int CountersPerDeath = 1;

    /// <summary>
    /// Construct Sengir Vampire with no live event-bus wiring (the shape /
    /// dispatcher path). Flying is attached; the damage-linked dies trigger
    /// is represented as an attached marker <see cref="TriggeredAbility"/>
    /// for shape, but the per-turn tracker / dies watcher / turn-start clear
    /// are NOT subscribed, so no counters accrue. Suitable for factory-shape
    /// / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, eventBus: null, replacements: null);

    /// <summary>
    /// Construct Sengir Vampire. When <paramref name="eventBus"/> is supplied
    /// the damage tracker, dies watcher, and per-turn clear subscribe so the
    /// "creature dealt damage by this creature this turn dies → +1/+1
    /// counter" loop runs end to end. When <paramref name="replacements"/>
    /// is supplied the counter placement routes through it so Hardened
    /// Scales / Doubling Season can rewrite the count (CR 614).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying. KeywordAbility marker; combat code reads it via
        // CombatAbilities.HasFlying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Damage-linked dies trigger — CR 603.1.
        //   "Whenever a creature dealt damage by this creature this turn
        //    dies, put a +1/+1 counter on this creature."
        //
        // Per-instance, per-turn set of victim InstanceIds. The printed
        // condition is "dealt damage" (CR 120) — combat AND non-combat both
        // count — so the tracker matches every DamageDealtEvent whose Source
        // is this card (CombatDamageDealtEvent is a subclass and flows
        // through the same handler).
        // ----------------------------------------------------------------
        var damagedThisTurn = new HashSet<Guid>();

        // Marker triggered ability so factory-shape / dispatch tests can
        // assert the dies trigger is attached. The actual firing is driven
        // by the event-bus subscriptions below (the marker effect is a
        // no-op placeholder — the bus handler performs the counter
        // placement so the per-turn linkage is enforced).
        var triggerMarker = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.FromZone == ZoneType.Battlefield
                && e.ToZone == ZoneType.Graveyard
                && e.Card is Creature
                && damagedThisTurn.Contains(e.Card.InstanceId)),
            effects: new IEffect[]
            {
                new Effect(
                    $"{CardName}: put a +1/+1 counter on it (a creature it damaged died)",
                    () => CountersService.Add(
                        card, CounterType.PlusOnePlusOne, CountersPerDeath, replacements, eventBus)),
            },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(triggerMarker);

        if (eventBus != null)
        {
            // CR 120 — record every creature this card deals damage to this
            // turn (combat or non-combat). Keyed on InstanceId so the id
            // survives the victim leaving the battlefield.
            //
            // EventBus.Publish dispatches on the STATIC generic type, not the
            // runtime type, so combat damage (published as
            // CombatDamageDealtEvent) and non-combat damage (published as the
            // base DamageDealtEvent) reach DIFFERENT subscriber lists. We
            // subscribe to BOTH so every "dealt damage" source is recorded
            // (CR 120 — the printed condition is "dealt damage", not "dealt
            // combat damage").
            void RecordVictim(DamageDealtEvent e)
            {
                if (!ReferenceEquals(e.SourceCard, card)) return;
                if (e.TargetCard is not Creature victim) return;
                damagedThisTurn.Add(victim.InstanceId);
            }

            eventBus.Subscribe<DamageDealtEvent>(RecordVictim);
            eventBus.Subscribe<CombatDamageDealtEvent>(RecordVictim);

            // CR 700.4 — a creature "dies" when it moves Battlefield →
            // Graveyard. If it was one of this card's victims this turn,
            // put a +1/+1 counter on Sengir Vampire (CR 122.1).
            eventBus.Subscribe<CardMovedEvent>(e =>
            {
                if (e.FromZone != ZoneType.Battlefield) return;
                if (e.ToZone != ZoneType.Graveyard) return;
                if (e.Card is not Creature) return;
                if (!damagedThisTurn.Remove(e.Card.InstanceId)) return;

                // Counter placement routes through the replacement bus
                // (Hardened Scales / Doubling Season — CR 614) and publishes
                // CounterAddedEvent so downstream payoffs chain.
                CountersService.Add(
                    card, CounterType.PlusOnePlusOne, CountersPerDeath, replacements, eventBus);
            });

            // CR 514.x — "this turn" scope. Clear the victim set at the
            // start of every turn.
            eventBus.Subscribe<TurnStartedEvent>(_ => damagedThisTurn.Clear());
        }

        return card;
    }
}
