using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 701.40 — Explore. A permanent (creature) explores: its controller
/// reveals the top card of their library; if it's a land card it goes into
/// their hand, otherwise the controller puts a +1/+1 counter on the exploring
/// permanent and then puts the revealed card either back on top of their
/// library or into their graveyard (their choice). An empty library reveals
/// nothing and only the +1/+1-counter branch happens.
///
/// <para>
/// CR 701.40a — "Certain abilities instruct a permanent to explore." CR
/// 701.40b — "the permanent's controller reveals the top card of their
/// library, then puts that card into their hand if it's a land card." CR
/// 701.40c — "Otherwise, that player puts a +1/+1 counter on the exploring
/// permanent, then puts the revealed card back on top of their library or
/// into their graveyard." CR 701.40d — "If no cards are in that library, that
/// permanent's controller still puts a +1/+1 counter on the exploring
/// permanent." CR 701.40e — "A permanent explores even if … it leaves the
/// battlefield"; the explore event is published once the action resolves so
/// "Whenever a creature you control explores" triggers (Wildgrowth Walker)
/// can fire.
/// </para>
///
/// <para>
/// The put-back/graveyard choice (CR 701.40c) consults the registered
/// <see cref="IPlayerAgent"/> via
/// <see cref="IPlayerAgent.ChooseExploreKeepOnTopAsync"/>; when no agent is
/// registered the card is left on top of the library (library-preserving
/// default). The +1/+1 counter is routed through
/// <see cref="CountersService.Add"/> so replacement effects (Hardened Scales,
/// Doubling Season) and "counter is put on" triggers observe it.
/// </para>
/// </summary>
public static class ExploreAction
{
    /// <summary>
    /// Resolve the explore action (CR 701.40) for <paramref name="creature"/>
    /// under <paramref name="controller"/>'s control. Reveals the top card of
    /// the controller's library and applies the land → hand / non-land →
    /// counter + keep-or-graveyard branches, then publishes a
    /// <see cref="CreatureExploredEvent"/> on the supplied (or registry)
    /// event bus.
    /// </summary>
    /// <param name="creature">The exploring permanent (CR 701.40a — the
    /// +1/+1 counter goes on this permanent).</param>
    /// <param name="controller">The exploring permanent's controller — whose
    /// library is revealed and who chooses keep-or-graveyard (CR 701.40b).</param>
    /// <param name="agent">Agent consulted for the keep-or-graveyard choice;
    /// falls back to <see cref="AgentRegistry"/> then keep-on-top.</param>
    /// <param name="game">Live game context passed to the agent prompt
    /// (nullable on the v1 sync-over-async closure path).</param>
    /// <param name="replacements">Replacement bus for the +1/+1 counter
    /// placement (Hardened Scales / Doubling Season); may be null.</param>
    /// <param name="eventBus">Bus to publish the explore event on; falls back
    /// to <see cref="EventBusRegistry.Get(Player?)"/>.</param>
    /// <param name="zones">Zone service for the reveal-card moves; falls back
    /// to <see cref="ZoneServiceRegistry.Get(Player?)"/> then raw-zone
    /// mutation.</param>
    public static async ValueTask ExploreAsync(
        ICard creature,
        Player controller,
        IPlayerAgent? agent,
        Majik.Core.Game.GameContext? game,
        ReplacementBus? replacements,
        IEventBus? eventBus,
        ZoneService? zones,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(creature);
        ArgumentNullException.ThrowIfNull(controller);

        var bus = eventBus ?? EventBusRegistry.Get(controller);
        var zoneSvc = zones ?? ZoneServiceRegistry.Get(controller);

        // CR 701.40b — reveal the top card of the controller's library (peek
        // only; the card stays on top until we decide where it goes).
        var revealed = controller.Zones.Library.GetCards().FirstOrDefault();

        if (revealed is null)
        {
            // CR 701.40d — empty library: still put a +1/+1 counter on the
            // exploring permanent. No card to move.
            PlaceCounter(creature, replacements, bus);
            bus?.Publish(new CreatureExploredEvent(creature, controller, revealedCard: null, revealedLand: false));
            return;
        }

        if (revealed.HasType(CardType.Land))
        {
            // CR 701.40b — land card goes to the controller's hand. No counter.
            MoveFromLibrary(revealed, controller, ZoneType.Hand, zoneSvc);
            bus?.Publish(new CreatureExploredEvent(creature, controller, revealed, revealedLand: true));
            return;
        }

        // CR 701.40c — non-land: +1/+1 counter on the exploring permanent,
        // then keep on top OR put into graveyard (controller's choice).
        PlaceCounter(creature, replacements, bus);

        var chooser = agent ?? AgentRegistry.Get(controller);
        var keepOnTop = chooser is null
            ? true // No agent — keep on top (library-preserving default).
            : await chooser.ChooseExploreKeepOnTopAsync(game, creature, revealed, ct)
                .ConfigureAwait(false);

        if (!keepOnTop)
        {
            MoveFromLibrary(revealed, controller, ZoneType.Graveyard, zoneSvc);
        }
        // keepOnTop: the card is already on top of the library — no move.

        bus?.Publish(new CreatureExploredEvent(creature, controller, revealed, revealedLand: false));
    }

    private static void PlaceCounter(ICard creature, ReplacementBus? replacements, IEventBus? bus)
    {
        // CR 701.40c / CR 122 — a single +1/+1 counter on the exploring
        // permanent. Routed through CountersService so replacement effects
        // and "counter is put on" triggers observe it (CR 614 / CR 121).
        if (creature is Permanent permanent)
        {
            CountersService.Add(permanent, CounterType.PlusOnePlusOne, 1, replacements, bus);
        }
    }

    private static void MoveFromLibrary(ICard card, Player controller, ZoneType dest, ZoneService? zones)
    {
        if (zones is not null)
        {
            zones.MoveCard(card, ZoneType.Library, dest, controller);
            return;
        }

        // Raw-zone fallback (shape / dispatcher tests without a registered
        // ZoneService) — mirrors RevealAndChoose's raw path.
        controller.Zones.Library.RemoveCard(card);
        var destZone = dest switch
        {
            ZoneType.Hand => controller.Zones.Hand,
            ZoneType.Graveyard => controller.Zones.Graveyard,
            _ => throw new InvalidOperationException(
                $"ExploreAction: unsupported destination zone {dest}."),
        };
        destZone.AddCard(card);
        card.SetZone(dest);
    }
}
