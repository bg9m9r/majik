using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fallen Shinobi (Modern Horizons 2, {3}{U}{B}).
///
/// Creature — Zombie Ninja 5/4. Oracle text (Scryfall, verified):
///   "Ninjutsu {2}{U}{B} ({2}{U}{B}, Return an unblocked attacker you control
///    to hand: Put this card onto the battlefield from your hand tapped and
///    attacking.)
///    Whenever this creature deals combat damage to a player, that player
///    exiles the top two cards of their library. Until end of turn, you may
///    play those cards without paying their mana costs."
///
/// The base shape (name, Creature — Zombie Ninja, {3}{U}{B}, 5/4) is
/// materialised from the embedded JSON definition (<c>fallen-shinobi.json</c>)
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> (same posture as
/// <see cref="KaitoBaneOfNightmaresFactory"/>). The Ninjutsu marker and the
/// combat-damage triggered ability are layered on here — the JSON
/// <c>AbilityDefinition</c> schema does not express Ninjutsu or the
/// exile-top-two / play-without-paying grant.
///
/// ## Implemented (v1)
/// - 5/4 Creature — Zombie Ninja at {3}{U}{B}.
/// - <b>Ninjutsu {2}{U}{B} (CR 702.49).</b> A <see cref="NinjutsuAbility"/>
///   marker records the printed ninjutsu mana cost ({2}{U}{B}). The reusable
///   <see cref="NinjutsuAction.Execute"/> primitive performs the special
///   action (return an unblocked attacker to hand, put this onto the
///   battlefield tapped and attacking) — same wiring as Kaito / Ninja of the
///   Deep Hours.
/// - <b>Combat-damage-to-a-player trigger (CR 510, CR 603.1)</b> wired over
///   <see cref="CombatDamageDealtEvent"/> filtered to the source card and a
///   non-null <see cref="CombatDamageDealtEvent.TargetPlayer"/>. On resolve:
///     1. exiles the top TWO cards of the damaged player's library (no-op /
///        partial when the library has fewer than two cards — empty-library
///        loss is the SBA's job, CR 104.3a / CR 704, not this effect);
///     2. stamps a runtime exile-cast grant on each exiled card via
///        <see cref="Card.GrantRuntimeExileCast"/> permitting the Fallen
///        Shinobi controller (NOT the card's owner — these are exiled from the
///        OPPONENT's library) to play it from exile for <see cref="ManaCost.Zero"/>
///        — "without paying their mana costs", CR 118.9 / CR 601.3b — until end
///        of turn. The matching alternative cost is
///        <see cref="Majik.Core.Costs.ExileCastAlternativeCost"/>;
///     3. when an <see cref="IEventBus"/> is supplied, a one-shot
///        <see cref="StepStartedEvent"/> handler clears both grants on the
///        first Cleanup step (CR 514.2) and unsubscribes itself.
///
/// ## How the granted cards are played
/// Pass <see cref="Majik.Core.Costs.ExileCastAlternativeCost"/> built from
/// the exiled card's <see cref="Card.RuntimeExileCastCost"/> (here
/// <see cref="ManaCost.Zero"/>) to <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>.
/// The alt cost rejects all callers other than
/// <see cref="Card.RuntimeExileCastAllowedCaster"/>; once the EOT subscription
/// clears the grant, every probe rejects.
///
/// ## Deferred (v1 gaps)
/// - <b>Playing lands among the exiled cards.</b> The oracle says "play"
///   (not "cast"), so a land among the top two could be played from exile.
///   The runtime exile-cast grant is a SPELL-cast permission (same shape
///   Ragavan uses); a land-play-from-exile permission is not yet modelled in
///   the engine, so a land in the exiled pair is exiled but not playable under
///   v1. This mirrors Ragavan's "you may cast that card" posture and is the
///   only oracle clause not fully surfaced.
/// - <b>"You may" decision before playing.</b> The grant + alt cost is the
///   permission layer; the actual decision to play belongs to the agent's
///   priority loop. No new prompt surface is introduced (same as Ragavan).
/// </summary>
[CardName("Fallen Shinobi")]
public static class FallenShinobiFactory
{
    public const string CardName = "Fallen Shinobi";
    public const string Slug = "fallen-shinobi";

    /// <summary>CR 702.49 — Fallen Shinobi's printed ninjutsu mana cost.</summary>
    public const string NinjutsuCost = "{2}{U}{B}";

    /// <summary>Number of cards the trigger exiles off the damaged player's
    /// library top (CR 603.1 — "the top two cards of their library").</summary>
    public const int CardsExiled = 2;

    /// <summary>
    /// Construct Fallen Shinobi with no live ZoneService / event-bus /
    /// TriggerManager wiring. The Ninjutsu marker + combat-damage trigger are
    /// attached for shape but the trigger is not registered; the exile move
    /// uses raw zone moves; the runtime exile-cast grants remain until the test
    /// clears them manually (no EOT cleanup subscription). Suitable for shape /
    /// dispatcher tests. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Fallen Shinobi with optional runtime services. When
    /// <paramref name="zoneService"/> is supplied the exile moves publish
    /// <see cref="CardMovedEvent"/>s; when <paramref name="eventBus"/> is
    /// supplied the runtime exile-cast grants are cleared on the next Cleanup
    /// step; when <paramref name="triggers"/> is supplied the combat trigger is
    /// registered so a <see cref="CombatDamageDealtEvent"/> automatically
    /// queues the ability.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature —
        // Zombie Ninja, {3}{U}{B}, 5/4). The JSON carries no abilities —
        // Ninjutsu + the combat trigger are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // -- Ninjutsu {2}{U}{B} (CR 702.49) ---------------------------------
        // Marker carrying the ninjutsu mana cost; the special action is
        // performed by NinjutsuAction.Execute (shared primitive).
        card.AddAbility(new NinjutsuAbility(card, NinjutsuCost, owner));

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510, CR 603.1.
        //   "Whenever this creature deals combat damage to a player, that
        //    player exiles the top two cards of their library. Until end of
        //    turn, you may play those cards without paying their mana costs."
        // The predicate captures the damaged player off the event so the
        // resolved effect targets the correct library at fire time (CR 603.3
        // evaluates the trigger condition before the ability hits the stack,
        // so the captured player is fresh by the time the effect resolves).
        // ----------------------------------------------------------------
        Player? capturedDamaged = null;

        var effect = new Effect(
            "Fallen Shinobi: damaged player exiles top two of their library + "
            + "play-without-paying EOT grant",
            () =>
            {
                var victim = capturedDamaged;
                if (victim == null) return;

                // Exile the top two cards of the damaged player's library.
                // Snapshot first so removals during the loop don't shift the
                // remaining top. Fewer than two cards ⇒ exile what's there
                // (empty-library loss is the SBA's job, not this effect).
                var topCards = victim.Zones.Library.GetCards()
                    .Take(CardsExiled)
                    .ToList();

                foreach (var top in topCards)
                {
                    if (zoneService != null)
                    {
                        zoneService.MoveCard(top, ZoneType.Library, ZoneType.Exile);
                    }
                    else
                    {
                        victim.Zones.Library.RemoveCard(top);
                        victim.Zones.Exile.AddCard(top);
                        top.SetZone(ZoneType.Exile);
                    }

                    // "Until end of turn, you may play those cards without
                    //  paying their mana costs." The Fallen Shinobi controller
                    //  (not the card's owner) is the allowed caster; cost is
                    //  ManaCost.Zero — CR 118.9 / CR 601.3b.
                    if (top is Card stampable)
                    {
                        stampable.GrantRuntimeExileCast(owner, ManaCost.Zero);
                    }
                }

                // EOT cleanup — CR 514.2 / CR 514.3. Schedule a one-shot
                // handler that clears every grant stamped above on the first
                // Cleanup step and unsubscribes. Skipped when no bus is wired
                // (callers manage EOT manually in tests).
                if (eventBus != null && topCards.Count > 0)
                {
                    Action<StepStartedEvent>? handler = null;
                    handler = (e) =>
                    {
                        if (e.StepType != PhaseStateType.Cleanup) return;
                        foreach (var top in topCards)
                        {
                            if (top is Card stampable) stampable.ClearRuntimeExileCast();
                        }
                        if (handler != null) eventBus.Unsubscribe(handler);
                    };
                    eventBus.Subscribe(handler);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
            {
                if (!ReferenceEquals(e.Source, card)) return false;
                if (e.TargetPlayer == null) return false;
                capturedDamaged = e.TargetPlayer;
                return true;
            }),
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
