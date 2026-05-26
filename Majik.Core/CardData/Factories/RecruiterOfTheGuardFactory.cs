using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Recruiter of the Guard (Conspiracy: Take the
/// Crown, {2}{W}).
///
/// Creature — Human Soldier 1/1. Oracle text:
///   "When Recruiter of the Guard enters, you may search your library for
///    a creature card with toughness 2 or less, reveal it, put it into
///    your hand, then shuffle."
///
/// ## Implemented (v1)
/// - 1/1 Human Soldier with mana cost {2}{W}.
/// - <b>ETB tutor (CR 603.1 / CR 701.19a)</b>: when Recruiter of the Guard
///   enters the battlefield, the controller's library is scanned for
///   creature cards with <see cref="Creature.Toughness"/> ≤ 2. The agent's
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> selects a pick
///   (deterministic first-match fallback when no agent is registered); a
///   null pick is a legal decline (CR 701.19a).
/// - When a pick is selected the chosen creature card is moved
///   Library → Hand and the library is shuffled via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a). Shape
///   mirrors <see cref="StoneforgeMysticFactory"/> +
///   <see cref="TrinketMageFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the picked card moves Library → Hand without
///   emitting a CardRevealedEvent; same gap as the other tutor factories.
/// - <b>Non-creature cards with toughness</b>: the predicate downcasts to
///   <see cref="Creature"/> to read the toughness value, which is the
///   pragmatic shape (only creature cards have toughness in the engine's
///   type model). The oracle reads "a creature card with toughness 2 or
///   less" so this is faithful.
/// </summary>
[CardName("Recruiter of the Guard")]
public static class RecruiterOfTheGuardFactory
{
    public const string CardName = "Recruiter of the Guard";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Recruiter of the Guard with no live TriggerManager wiring
    /// (the shape/dispatcher path). The ETB trigger is attached but not
    /// registered — suitable for unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Recruiter of the Guard with optional runtime services.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
    /// to the battlefield places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = eventBus; // reserved for parity / future reveal-event wiring.

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Recruiter of the Guard enters, you may search your
        //    library for a creature card with toughness 2 or less, reveal
        //    it, put it into your hand, then shuffle."
        // Predicate: HasType(Creature) AND Toughness ≤ 2. Toughness reads
        // from the Creature type's Toughness property; ICard candidates
        // that aren't Creature subclasses are filtered out by the type
        // predicate.
        // CR 701.20a shuffle wired via LibraryShuffle.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor a creature (toughness ≤ 2) to hand",
            () =>
            {
                var controller = card.Controller ?? owner;

                var candidates = controller.Zones.Library.GetCards()
                    .OfType<Creature>()
                    .Where(c => c.Toughness <= 2)
                    .Cast<ICard>()
                    .ToList();
                if (candidates.Count == 0) return; // CR 701.19a — empty = no-op.

                var agent = AgentRegistry.Get(controller);
                ICard? pick = agent != null
                    ? agent.ChooseLibraryPickAsync(
                        ctx: null,
                        candidates,
                        "creature card with toughness 2 or less")
                        .GetAwaiter().GetResult()
                    : candidates[0];
                if (pick == null) return; // CR 701.19a — caster declined.

                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                // CR 701.20a — shuffle after the search resolves.
                LibraryShuffle.ShuffleLibrary(controller, "recruiter-of-the-guard");
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
