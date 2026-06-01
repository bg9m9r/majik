using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Badgermole Cub (Bloomburrow).
///
/// Creature — Bear {G} 1/1. Oracle text:
///   "When this creature enters, earthbend 1. (Target land you control becomes
///    a 0/0 creature with haste that's still a land. Put a +1/+1 counter on it.
///    When it dies or is exiled, return it to the battlefield tapped.)
///    Whenever you tap a creature for mana, add an additional {G}."
///
/// ## Implemented
/// - Correct name, type (Creature), subtype (Bear), mana cost ({G}),
///   power/toughness (1/1).
/// - <b>Earthbend 1 ETB</b> (CR 701.59 / 603.6a): an ETB
///   <see cref="TriggeredAbility"/> with a unified
///   <see cref="TargetRequest"/> for "target land you control". On
///   resolution it routes the chosen land through
///   <see cref="EarthbendAction.Apply(Land, Player, int, ContinuousEffectsService?)"/>
///   — the land gets a +1/+1 counter and is animated into a 0/0 Elemental
///   creature with haste that's still a land (so a 1/1 with the counter,
///   surfacing through the layer system's creature-row upgrade). The live
///   <see cref="ContinuousEffectsService"/> is read from this card's
///   <see cref="Permanent.ActiveEffects"/> at resolution (the prod build path
///   wires it). The TriggerManager auto-binds this card on its ETB
///   <see cref="CardMovedEvent"/>, so the trigger fires in a real match
///   without explicit registration.
///
/// ## Deferred (v1 gap)
/// - <b>"Whenever you tap a creature for mana, add {G}"</b>: tap-for-mana
///   watcher missing (separate deferral surface — not Earthbend).
/// </summary>
[CardName("Badgermole Cub")]
public static class BadgermoleCubFactory
{
    public const string CardName = "Badgermole Cub";

    public static CardDef Define() => CardDef
        .Creature(CardName, "{G}", power: 1, toughness: 1)
        .WithSubtype(CardSubtype.Bear);

    /// <summary>
    /// Build Badgermole Cub with its Earthbend-1 ETB trigger attached. Used by
    /// the source-generated dispatcher and the prod routed build path
    /// (<c>NamedCardFactory.Create</c>). The ETB trigger targets a land the
    /// controller controls and earthbends it on resolution; the live
    /// <see cref="ContinuousEffectsService"/> is resolved from the card's
    /// <see cref="Permanent.ActiveEffects"/> when the animate effects register.
    /// </summary>
    public static Creature Create(Player owner)
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

                if (chosen[0][0] is not Land land) return;
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
                    CandidateGatherer: ctx =>
                    {
                        var controller = card.Controller ?? owner;
                        return controller.Zones.Battlefield.GetCards()
                            .OfType<Land>()
                            .Cast<object>()
                            .ToList();
                    }),
            });

        card.AddAbility(etbTrigger);

        return card;
    }
}
