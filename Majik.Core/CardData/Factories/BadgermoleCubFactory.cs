using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Badgermole Cub (Bloomburrow).
///
/// Creature — Badger Mole {G} 2/2. Oracle text:
///   "When this creature enters, earthbend 1. (Target land you control becomes
///    a 0/0 creature with haste that's still a land. Put a +1/+1 counter on it.
///    When it dies or is exiled, return it to the battlefield tapped.)
///    Whenever you tap a creature for mana, add an additional {G}."
///
/// ## Implemented
/// - Correct name, type (Creature), no enum-backed subtype (printed
///   "Badger Mole" has no CardSubtype value), mana cost ({G}),
///   power/toughness (2/2).
/// - <b>Earthbend 1 ETB</b> (CR 701.59 / 603.6a): an ETB
///   <see cref="TriggeredAbility"/> with a unified
///   <see cref="TargetRequest"/> for "target land you control". On
///   resolution it routes the chosen land through
///   <see cref="EarthbendAction.Apply(Permanent, Player, int, ContinuousEffectsService?)"/>
///   — the land gets a +1/+1 counter and is animated into a 0/0
///   creature with haste that's still a land (so a 0/0 base +1/+1 = 1/1,
///   surfacing through the layer system's creature-row upgrade). The live
///   <see cref="ContinuousEffectsService"/> is read from this card's
///   <see cref="Permanent.ActiveEffects"/> at resolution (the prod build path
///   wires it). The TriggerManager auto-binds this card on its ETB
///   <see cref="CardMovedEvent"/>, so the trigger fires in a real match
///   without explicit registration.
///
/// - <b>"Whenever you tap a creature for mana, add an additional {G}"</b>
///   (CR 605.1b — a triggered mana ability that triggers on mana being
///   produced and itself produces mana): a <see cref="TriggeredAbility"/>
///   subscribing to <see cref="ManaAbilityActivatedEvent"/> (published by
///   <see cref="Majik.Core.Services.ManaAbilityActivator"/> after the
///   activator's pool is topped up — the same surface Utopia Sprawl /
///   Mirari's Wake consume). The condition matches when the activator is
///   THIS card's controller ("you", CR 109.5) AND the tapped source is a
///   <see cref="Creature"/>. The effect adds an additional {G} to that
///   controller's mana pool via <see cref="Player.AddManaToPool"/>.
/// </summary>
[CardName("Badgermole Cub")]
public static class BadgermoleCubFactory
{
    public const string CardName = "Badgermole Cub";

    public static CardDef Define() => CardDef
        .Creature(CardName, "{1}{G}", power: 2, toughness: 2);

    /// <summary>
    /// Build Badgermole Cub with its Earthbend-1 ETB trigger attached. Used by
    /// the source-generated dispatcher and the prod routed build path
    /// (<c>NamedCardFactory.Create</c>). The ETB trigger targets a land the
    /// controller controls and earthbends it on resolution; the live
    /// <see cref="ContinuousEffectsService"/> is resolved from the card's
    /// <see cref="Permanent.ActiveEffects"/> when the animate effects register.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Build a fully-wired Badgermole Cub. The Earthbend-1 ETB trigger and the
    /// "whenever you tap a creature for mana, add an additional {G}" triggered
    /// mana ability are both attached to the card's
    /// <see cref="Card.Abilities"/> collection; when <paramref name="triggers"/>
    /// is supplied the tap-for-mana trigger is also registered with the
    /// <see cref="TriggerManager"/> so it surfaces as pending end-to-end (the
    /// ETB trigger auto-binds on its <see cref="CardMovedEvent"/>).
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefRuntime.Build(Define(), owner);
        card.SetController(owner);

        TriggeredAbility? etbTrigger = null;

        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            "Badgermole Cub — earthbend 1 (animate target land you control)",
            () =>
            {
                if (etbTrigger == null) return;

                var chosen = etbTrigger.ChosenTargets;
                if (chosen.Count == 0 || chosen[0].Count == 0) return;

                // "target land you control" — any permanent whose types
                // include Land, INCLUDING a Land Creature (Dryad Arbor) built
                // as a Creature C# instance. Filter on the land TYPE, not the
                // Land C# class (CR 701.59).
                if (chosen[0][0] is not Permanent land
                    || !land.HasType(CardType.Land)) return;
                // CR 608.2b — target must still be a land the controller
                // controls on the battlefield at resolution.
                if (land.Zone != ZoneType.Battlefield) return;

                var controller = card.Controller ?? owner;

                // Earthbend 1 (CR 701.59). The live CES drives the animate
                // continuous effect; EarthbendAction falls back to the land's
                // ActiveEffects when card.ActiveEffects is null.
                EarthbendAction.Apply(land, controller, 1, card.ActiveEffects);
            });

        etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield },
            targetRequests: new[]
            {
                new TargetRequest(
                    Description: "target land you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.None,
                    // "target land you control" — only the controller's lands.
                    // Filter on the land TYPE, not the Land C# class, so a Land
                    // Creature the controller controls (Dryad Arbor, built as a
                    // Creature instance) is offered too (CR 701.59 / 115.4).
                    CandidateGatherer: ctx =>
                    {
                        var controller = card.Controller ?? owner;
                        return controller.Zones.Battlefield.GetCards()
                            .OfType<Permanent>()
                            .Where(p => p.HasType(CardType.Land))
                            .Cast<object>()
                            .ToList();
                    }),
            });

        card.AddAbility(etbTrigger);

        // --------------------------------------------------------------------
        // "Whenever you tap a creature for mana, add an additional {G}."
        // CR 605.1b — a triggered mana ability (triggers on mana being
        // produced; itself produces mana). It subscribes to the
        // ManaAbilityActivatedEvent published by ManaAbilityActivator after the
        // activator's pool is topped up (same surface Utopia Sprawl consumes).
        // CR 109.5 / 603.2 — "you" is THIS card's controller; the trigger only
        // fires when the controller is the player who tapped a creature.
        // --------------------------------------------------------------------
        var bonusGreen = ManaCost.Parse("G");
        Player? pendingController = null;

        var tapCondition = new EventTriggerCondition<ManaAbilityActivatedEvent>((e, _) =>
        {
            // "you tap" — the activator must be the cub's current controller.
            var you = card.Controller ?? owner;
            if (!ReferenceEquals(e.Player, you)) return false;
            // "a creature for mana" — the tapped source must be a creature.
            // Use IsEffectivelyCreature() rather than `is Creature` so that
            // a land animated by Earthbend (a Land C# instance with a Layer-4
            // creature-type grant) also counts (CR 613.7b / 701.59).
            if (e.Source is not Permanent p || !p.IsEffectivelyCreature()) return false;
            pendingController = e.Player;
            return true;
        });

        var addGreenEffect = new Effect(
            "Badgermole Cub — add an additional {G}",
            () =>
            {
                var controller = pendingController;
                pendingController = null;
                controller?.AddManaToPool(bonusGreen);
            });

        var tapTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: tapCondition,
            effects: new IEffect[] { addGreenEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(tapTrigger);
        triggers?.RegisterTriggeredAbility(tapTrigger);

        return card;
    }
}
