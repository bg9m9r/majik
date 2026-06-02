using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Subterranean Schooner (Lost Caverns of Ixalan,
/// {1}{U}).
///
/// Artifact — Vehicle 3/4. Oracle text:
///   "Whenever this Vehicle attacks, target creature that crewed it this
///    turn explores. (Reveal the top card of your library. Put that card
///    into your hand if it's a land. Otherwise, put a +1/+1 counter on that
///    creature, then put the card back or put it into your graveyard.)"
///   "Crew 1"
///
/// ## Implementation
///
/// - Shell follows the Vehicle MVP convention (mirrors
///   <see cref="EsikasChariotFactory"/> / <see cref="SmugglersCopterFactory"/>):
///   a <see cref="Creature"/> with <see cref="CardType.Artifact"/> additively
///   stamped (CR 301.1 / 302.1 — the "Artifact Vehicle" multi-type pattern).
///   Base P/T 3/4 — <see cref="CardData.Vehicles.CrewAction"/> ships this
///   through <see cref="Majik.Core.Effects.VehicleCrewEffect"/> when crewed.
///   Not legendary (the printed face has no Legendary supertype).
/// - <b>Attack-explore trigger</b> (CR 508.1f, CR 603.1, CR 701.40):
///   "Whenever this Vehicle attacks, target creature that crewed it this
///   turn explores." Wired via <see cref="Triggers.OnAttackSelf"/>; the
///   chosen creature explores through the shared
///   <see cref="Majik.Core.Keywords.ExploreAction.ExploreAsync"/> resolver,
///   so the land → hand / non-land → +1/+1 counter + keep-or-graveyard
///   branches and the <c>CreatureExploredEvent</c> publication are handled
///   exactly like the ETB-explore family (Seekers' Squire, Merfolk
///   Branchwalker). The +1/+1 counter (CR 701.40c) lands on the chosen
///   creature, NOT on the Vehicle.
/// - <b>Crew 1</b> (CR 702.122): surfaced as <see cref="CrewCost"/>; callers
///   route through <see cref="CardData.Vehicles.CrewAction.Crew"/> exactly
///   as Esika's Chariot / Smuggler's Copter do.
///
/// ## Deferred (v1 gaps)
/// - <b>"creature that crewed it this turn" tracking</b>: the engine does not
///   yet record which creatures tapped to crew which Vehicle this turn (CR
///   702.122e). Following the Esika's Chariot v1 targeting pattern, the
///   attack trigger takes an injected <c>explorerPicker</c> that supplies the
///   exploring creature (tests / bots scope it to a creature that crewed the
///   Schooner this turn). The deterministic fallback picks the first other
///   creature the controller controls. The "crewed it this turn" restriction
///   itself is enforced by the caller, not the trigger — consistent with the
///   rest of the Vehicle MVP, where crew is invoked directly rather than
///   through a full activated-ability surface.
/// - <b>Targeting legality</b>: the trigger is a "target" trigger (CR 603.3d);
///   v1 resolves the chosen creature eagerly via the picker rather than
///   through the full targeting subsystem, mirroring Esika's Chariot's
///   <c>copyTargetPicker</c>.
/// </summary>
[CardName("Subterranean Schooner")]
public static class SubterraneanSchoonerFactory
{
    public const string CardName = "Subterranean Schooner";
    public const string PrintedManaCost = "{1}{U}";
    public const int CrewCost = 1;
    public const int VehiclePower = 3;
    public const int VehicleToughness = 4;

    /// <summary>
    /// Construct Subterranean Schooner with no live wiring. The attack-explore
    /// trigger is attached to the card shape but not registered with a trigger
    /// manager; the explorer falls back to the deterministic first-other-
    /// creature pick on the controller's battlefield. Suitable for dispatcher /
    /// structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, explorerPicker: null);

    /// <summary>
    /// Construct Subterranean Schooner with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the attack-explore trigger is
    /// registered so a bus-driven <see cref="CreatureAttacksEvent"/> for this
    /// Vehicle places it on the stack automatically. When
    /// <paramref name="explorerPicker"/> is supplied it is invoked at
    /// resolution to choose which creature (a creature that crewed this
    /// Vehicle this turn — CR 702.122e) explores.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        Func<Player, Creature?>? explorerPicker)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: VehiclePower,
            toughness: VehicleToughness,
            subtypes: new[] { CardSubtype.Vehicle });

        // CR 301.1 / 302.1 — Subterranean Schooner is an Artifact (Vehicle).
        // The base Creature constructor only registers CardType.Creature, so
        // additively flag the Artifact type for HasType-based lookups
        // (mirrors Esika's Chariot / Smuggler's Copter).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Attack-explore trigger — CR 508.1f / 603.1 / 701.40.
        //   "Whenever this Vehicle attacks, target creature that crewed it
        //    this turn explores."
        // The chosen creature explores (NOT the Vehicle), so the CR 701.40c
        // +1/+1 counter lands on the chosen creature.
        // ----------------------------------------------------------------
        var exploreEffect = new Effect(
            $"{CardName}: target creature that crewed it this turn explores",
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                var explorer = explorerPicker?.Invoke(controller)
                    ?? DefaultPickCrewmate(controller, vehicle: card);
                if (explorer is null) return;

                await ExploreAction.ExploreAsync(
                    creature: explorer,
                    controller: controller,
                    agent: ctx.Agent ?? AgentRegistry.Get(controller),
                    game: ctx.Game,
                    replacements: null,
                    eventBus: eventBus,
                    zones: ZoneServiceRegistry.Get(controller),
                    ct: ctx.Ct).ConfigureAwait(false);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { exploreEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        return card;
    }

    /// <summary>
    /// Deterministic v1 fallback picker — the first creature the controller
    /// controls other than the Vehicle itself. "Target creature that crewed
    /// it this turn" is scoped here to "some other creature the controller
    /// controls" because the engine does not yet track per-Vehicle crew
    /// membership (CR 702.122e); production callers supply
    /// <c>explorerPicker</c> to enforce the crewed-this-turn restriction.
    /// </summary>
    private static Creature? DefaultPickCrewmate(Player controller, Creature vehicle)
    {
        return controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => !ReferenceEquals(c, vehicle));
    }
}
