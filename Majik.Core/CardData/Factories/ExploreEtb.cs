using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Shared wiring for the "When this creature enters, it explores" ETB shape
/// (CR 603.6a + CR 701.40) used by Seekers' Squire, Merfolk Branchwalker and
/// the rest of the ETB-explore family. Centralises the
/// <see cref="TriggeredAbility"/> construction + the explore resolution body
/// so each factory only supplies its identity.
/// </summary>
internal static class ExploreEtb
{
    /// <summary>
    /// Attach an unconditional self-ETB "it explores" triggered ability to
    /// <paramref name="card"/>. <paramref name="exploreCount"/> explores in
    /// sequence (CR 701.40) — Jadelight Ranger uses 2 ("explores, then it
    /// explores again"); the default is 1. The exploring permanent is always
    /// <paramref name="card"/> itself, so the non-land +1/+1 counter lands on
    /// it. When <paramref name="triggers"/> is supplied the trigger is
    /// registered so the relevant <c>CardMovedEvent</c> stacks it (CR 603.3).
    /// </summary>
    public static TriggeredAbility Attach(
        Creature card, Player owner, TriggerManager? triggers, int exploreCount = 1)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(owner);
        if (exploreCount < 1) throw new ArgumentOutOfRangeException(nameof(exploreCount));

        var label = exploreCount == 1
            ? $"{card.Name}: explore (when this creature enters)"
            : $"{card.Name}: explore {exploreCount}x (when this creature enters)";

        var etbEffect = new Effect(
            label,
            async ctx =>
            {
                var controller = card.Controller ?? owner;
                for (var i = 0; i < exploreCount; i++)
                {
                    await ExploreAction.ExploreAsync(
                        creature: card,
                        controller: controller,
                        agent: ctx.Agent ?? AgentRegistry.Get(controller),
                        game: ctx.Game,
                        replacements: null,
                        eventBus: null,
                        zones: ZoneServiceRegistry.Get(controller),
                        ct: ctx.Ct).ConfigureAwait(false);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return etbTrigger;
    }
}
