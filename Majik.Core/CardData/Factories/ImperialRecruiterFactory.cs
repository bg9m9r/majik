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
/// Named-card factory for Imperial Recruiter (Portal Three Kingdoms,
/// {2}{R}).
///
/// Creature — Human Advisor 1/1. Oracle text:
///   "When Imperial Recruiter enters, search your library for a creature
///    card with power 2 or less, reveal it, put it into your hand, then
///    shuffle."
///
/// ## Implemented (v1)
/// - 1/1 Human Advisor with mana cost {2}{R}.
/// - <b>ETB tutor (CR 603.1 / CR 701.19a)</b>: when Imperial Recruiter
///   enters the battlefield, the controller's library is scanned for
///   creature cards with <see cref="Creature.Power"/> ≤ 2. Note this
///   trigger is <b>not</b> a "may" — the printed oracle is mandatory; the
///   controller must search if able. Per CR 701.19a the controller may
///   still fail to find (a null agent pick is treated as a legal failure
///   to find — the engine surface is single-pick and a forced empty
///   selection collapses to the same no-op shape).
/// - When a pick is selected the chosen creature card is moved
///   Library → Hand and the library is shuffled via
///   <see cref="LibraryShuffle.ShuffleLibrary"/> (CR 701.20a). Shape
///   mirrors <see cref="RecruiterOfTheGuardFactory"/> with the predicate
///   pivoted from toughness to power.
///
/// ## Deferred (v1 gaps)
/// - <b>Reveal event</b>: the picked card moves Library → Hand without
///   emitting a CardRevealedEvent; same gap as the other tutor factories.
/// - <b>Mandatory-search semantics</b>: the oracle is mandatory ("search"
///   not "you may search"), and CR 701.19a still allows failing to find;
///   the deterministic-fallback picker takes the first eligible candidate
///   so the mandatory shape is honoured.
/// </summary>
[CardName("Imperial Recruiter")]
public static class ImperialRecruiterFactory
{
    public const string CardName = "Imperial Recruiter";
    public const string PrintedManaCost = "{2}{R}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Imperial Recruiter with no live TriggerManager wiring
    /// (the shape/dispatcher path). The ETB trigger is attached but not
    /// registered — suitable for unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Imperial Recruiter with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
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
            subtypes: new[] { CardSubtype.Human, CardSubtype.Advisor });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Imperial Recruiter enters, search your library for a
        //    creature card with power 2 or less, reveal it, put it into
        //    your hand, then shuffle."
        // Predicate: HasType(Creature) AND Power ≤ 2. Mandatory search
        // (not "may"); CR 701.19a still permits failing to find.
        // CR 701.20a shuffle wired via LibraryShuffle.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: tutor a creature (power ≤ 2) to hand",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                var candidates = controller.Zones.Library.GetCards()
                    .OfType<Creature>()
                    .Where(c => c.Power <= 2)
                    .Cast<ICard>()
                    .ToList();
                if (candidates.Count == 0) return; // CR 701.19a — failure to find = no-op.

                var agent = ctx.Agent ?? AgentRegistry.Get(controller);
                ICard? pick = agent != null
                    ? (await agent.ChooseLibraryPickAsync( ctx: ctx.Game,
                        candidates,
                        "creature card with power 2 or less").ConfigureAwait(false))
                    : candidates[0];
                if (pick == null) return; // CR 701.19a — failure to find.

                controller.Zones.Library.RemoveCard(pick);
                controller.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                // CR 701.20a — shuffle after the search resolves.
                LibraryShuffle.ShuffleLibrary(controller, "imperial-recruiter");
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
