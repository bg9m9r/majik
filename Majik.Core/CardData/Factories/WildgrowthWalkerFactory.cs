using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wildgrowth Walker (Guilds of Ravnica, {1}{G}).
/// Creature — Elemental 1/3.
///
/// Oracle text (verified against Scryfall):
///   "Whenever a creature you control explores, put a +1/+1 counter on this
///    creature and you gain 3 life."
///
/// ## Implemented (v1)
/// - 1/3 Elemental, mana cost {1}{G}, owner / controller wired.
/// - <b>Explore payoff trigger</b> (CR 603.1 + CR 701.40e): an
///   <see cref="EventTriggerCondition{CreatureExploredEvent}"/> filtered to
///   explores whose controller equals Wildgrowth Walker's controller
///   (CR 109.5 — "a creature you control explores"; the controller is
///   resolved live so a control change carries the trigger). On resolution:
///   a +1/+1 counter on Wildgrowth Walker (routed through
///   <see cref="CountersService.Add"/> so Hardened Scales / Doubling Season
///   replacements + "counter is put on" triggers observe it) AND the
///   controller gains 3 life (CR 119.3). Note the exploring creature itself
///   may have already received its own +1/+1 counter from the explore
///   non-land branch (CR 701.40c) — that counter goes on the EXPLORING
///   creature; this trigger's counter goes on Wildgrowth Walker.
///   Wildgrowth Walker triggers off its OWN explore too (it is a "creature
///   you control").
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. The payoff trigger is
///   attached but not registered with a <see cref="TriggerManager"/>; tests
///   fire the effect directly (same posture as <see cref="GuideOfSoulsFactory"/>).
/// - <see cref="Create(Player, TriggerManager?)"/> — registers the trigger so
///   a published <see cref="CreatureExploredEvent"/> stacks it (CR 603.3).
/// </summary>
[CardName("Wildgrowth Walker")]
public static class WildgrowthWalkerFactory
{
    public const string CardName = "Wildgrowth Walker";
    public const string PrintedManaCost = "{1}{G}";
    public const int Power = 1;
    public const int Toughness = 3;
    public const int LifeGain = 3;

    public static Creature Create(Player owner) => Create(owner, triggers: null);

    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Elemental });

        card.SetOwner(owner);
        card.SetController(owner);

        // --------------------------------------------------------------------
        // Explore payoff trigger — CR 603.1 + CR 701.40e.
        //   "Whenever a creature you control explores, put a +1/+1 counter on
        //    this creature and you gain 3 life."
        //
        // Fires on CreatureExploredEvent whose Controller is Wildgrowth
        // Walker's controller (CR 109.5 — "a creature you control"; resolved
        // live so a control change carries the trigger).
        // --------------------------------------------------------------------
        var condition = new EventTriggerCondition<CreatureExploredEvent>(
            (e, _) => ReferenceEquals(e.Controller, card.Controller));

        var effect = new Effect(
            $"{CardName} — +1/+1 counter on this creature and gain {LifeGain} life",
            () =>
            {
                var controller = card.Controller ?? owner;

                // CR 122 — +1/+1 counter on Wildgrowth Walker. Routed through
                // CountersService so replacement effects + "counter is put on"
                // triggers observe it (event bus looked up from the registry).
                CountersService.Add(
                    card, CounterType.PlusOnePlusOne, 1,
                    replacements: null,
                    eventBus: EventBusRegistry.Get(controller));

                // CR 119.3 — controller gains 3 life.
                controller.GainLife(LifeGain);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
