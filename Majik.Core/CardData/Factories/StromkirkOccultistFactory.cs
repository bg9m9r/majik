using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stromkirk Occultist (Eldritch Moon, {2}{R}).
///
/// Creature — Vampire Horror 3/2. Oracle text (verified against Scryfall
/// 2026-06-14):
///   "Trample
///    Whenever this creature deals combat damage to a player, exile the top
///    card of your library. Until end of turn, you may play that card.
///    Madness {1}{R}"
///
/// ## Shape source
/// Card identity (name, {2}{R}, 3/2, Creature — Vampire Horror, Trample
/// keyword) is loaded from <c>Majik.Core/CardData/Cards/stromkirk-occultist.json</c>
/// via <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built
/// through <see cref="CardDefinitionFactory"/> — the JSON <c>keywords</c> array
/// carries Trample (CR 702.19), honoured by
/// <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/>. The
/// combat-damage trigger is attached in code below.
///
/// ## Implemented (v1)
///
/// - <b>3/2 Creature — Vampire Horror at {2}{R}, with Trample.</b>
///
/// - <b>Combat-damage-to-a-player trigger (CR 510 / CR 603.1).</b>
///   "Whenever this creature deals combat damage to a player, exile the top
///   card of your library. Until end of turn, you may play that card." Fires
///   on a <see cref="CombatDamageDealtEvent"/> whose <c>Source</c> is this card
///   AND whose <see cref="DamageDealtEvent.TargetPlayer"/> is non-null (damage
///   to a creature does NOT fire — mirrors <see cref="BloodmadVampireFactory"/>
///   / <see cref="RagavanNimblePilfererFactory"/>). On resolution the top card
///   of the CONTROLLER's library is exiled and stamped with a runtime
///   exile-cast grant (<see cref="Card.GrantRuntimeExileCast"/>) for its
///   printed mana cost, allowed caster = the controller; the grant clears at
///   end of turn (CR 514.2) when an event bus is wired. Same impulse shape as
///   Ragavan, but the exiled card comes from the controller's OWN library and
///   the grant is "this turn" (Ragavan exiles the defending player's card).
///
/// ## Madness (NOT wired here — intrinsic)
/// Madness {1}{R} works intrinsically for every catalogued card (CR 702.35)
/// via <see cref="Majik.Core.Keywords.MadnessCatalog"/> consulted by the
/// central discard funnel <see cref="Majik.Core.Primitives.Fx.DiscardCard"/>;
/// "Stromkirk Occultist" is catalogued at {1}{R}, so the madness line needs no
/// factory code.
///
/// ## Lifecycle
///
/// - <see cref="Create(Player)"/> — shape only. The trigger ability is
///   attached (auto-registered by the engine's <see cref="TriggerManager"/> on
///   battlefield entry); callers may invoke the effect directly in tests.
/// - <see cref="Create(Player, TriggerManager?, IEventBus?)"/> — registers the
///   combat-damage trigger; the end-of-turn cleanup of the exile-cast grant
///   rides the supplied bus's Cleanup <see cref="StepStartedEvent"/>.
/// </summary>
[CardName("Stromkirk Occultist")]
public static class StromkirkOccultistFactory
{
    public const string CardName = "Stromkirk Occultist";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("stromkirk-occultist");

    /// <summary>
    /// Construct Stromkirk Occultist with no live <see cref="TriggerManager"/>
    /// wiring. The combat-damage trigger is attached for shape (and is
    /// auto-registered on battlefield entry by the engine's TriggerManager).
    /// </summary>
    public static Creature Create(Player owner)
        => Create(owner, triggers: null, eventBus: null);

    /// <summary>
    /// Construct Stromkirk Occultist. When <paramref name="triggers"/> is
    /// supplied the combat-damage trigger is registered so a
    /// <see cref="CombatDamageDealtEvent"/> from this card to a player
    /// automatically queues the ability. When <paramref name="eventBus"/> is
    /// supplied the exile-cast grant's end-of-turn cleanup is scheduled on the
    /// next Cleanup step (CR 514.2).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers, IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Combat-damage-to-a-player trigger — CR 510, CR 603.1.
        //   "Whenever this creature deals combat damage to a player, exile the
        //    top card of your library. Until end of turn, you may play that
        //    card."
        // Fires only when this card deals combat damage to a PLAYER
        // (TargetPlayer != null); damage to a creature does not match.
        // The exiled card comes from the CONTROLLER's own library.
        // ----------------------------------------------------------------
        var impulseEffect = new Effect(
            $"{CardName}: exile top of your library + may-play-it-this-turn grant",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                var controller = card.Controller ?? owner;

                // Exile the top card of the controller's own library.
                // Empty-library is a no-op (CR 120.3 — SBAs handle the
                // loss-condition, not this effect).
                var top = controller.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;

                controller.Zones.Library.RemoveCard(top);
                controller.Zones.Exile.AddCard(top);
                top.SetZone(ZoneType.Exile);

                if (top is Card stampable)
                {
                    // CR 118.9 — "you may play that card." Grant matches the
                    // exile-cast alternative cost; cost = the card's printed
                    // mana cost; allowed caster = the controller.
                    stampable.GrantRuntimeExileCast(controller, stampable.ManaCostValue);

                    // CR 514.2 — "until end of turn". Schedule a one-shot
                    // handler that clears the grant on the first Cleanup step
                    // and unsubscribes. Skipped when no bus is wired (tests
                    // manage EOT manually).
                    if (eventBus != null)
                    {
                        Action<StepStartedEvent>? handler = null;
                        handler = (e) =>
                        {
                            if (e.StepType != StepStateType.Cleanup) return;
                            stampable.ClearRuntimeExileCast();
                            if (handler != null) eventBus.Unsubscribe(handler);
                        };
                        eventBus.Subscribe(handler);
                    }
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CombatDamageDealtEvent>((e, _) =>
                ReferenceEquals(e.Source, card) && e.TargetPlayer != null),
            effects: new IEffect[] { impulseEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
