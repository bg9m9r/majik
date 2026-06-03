using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.152 — Blitz. Produces the three printed riders that pair with the
/// blitz keyword on every blitz creature (CR 702.152b):
///
///   1. The creature gains haste.
///   2. "When this creature dies, draw a card."
///   3. Its controller sacrifices it at the beginning of the next end step.
///
/// All three are gated on <see cref="Creature.BlitzWasPaid"/> (set by
/// <see cref="Majik.Core.Costs.BlitzAlternativeCost.OnResolved"/> only when the
/// spell was cast for its blitz cost — CR 702.152c). A creature cast for its
/// normal mana cost leaves the flag false and gets none of the riders, so this
/// factory is harmless to attach generically.
///
/// Mirror posture of <see cref="EvokeFactory"/> (which attaches one
/// <c>EvokeWasPaid</c>-gated ETB sacrifice). Blitz differs in that the haste
/// grant is a continuous characteristic (so it's expressed as a marker the
/// gated combat path reads, plus a summoning-sickness clear at ETB) and the
/// sacrifice is delayed to the next end step rather than immediate.
/// </summary>
public static class BlitzFactory
{
    /// <summary>Granted keyword. CR 702.10 — Haste.</summary>
    public const string HasteKeyword = "Haste";

    /// <summary>
    /// Build the dies → draw a card trigger (CR 702.152b, second rider). Fires
    /// when the source creature moves Battlefield → Graveyard (CR 700.4 —
    /// "dies"), but only when blitz was paid (intervening-if on
    /// <see cref="Creature.BlitzWasPaid"/>, re-checked when it would go on the
    /// stack — CR 603.4). Active in both Battlefield and Graveyard because
    /// <see cref="ZoneService"/> stamps <c>card.Zone = Graveyard</c> before
    /// publishing the <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
    /// (mirrors Aven Fisher / Stitcher's Supplier).
    /// </summary>
    public static TriggeredAbility BuildDiesTrigger(Creature source)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));

        var diesEffect = new Effect(
            "Blitz — when this creature dies, draw a card (CR 702.152b)",
            () =>
            {
                var controller = source.Controller ?? source.Owner;
                if (controller == null) return;
                // CR 121.1 — draw a card. Routes through Fx.DrawCards so any
                // active draw-replacement (e.g. Dredge) can intercept (CR 614).
                Fx.DrawCards(controller, 1);
            });

        return new TriggeredAbility(
            source,
            source.Controller ?? source.Owner
                ?? throw new InvalidOperationException("Blitz source must have a controller or owner"),
            condition: Triggers.OnDies(source),
            effects: new[] { diesEffect },
            interveningIf: () => source.BlitzWasPaid,
            // Battlefield + Graveyard: zone is stamped to Graveyard before the
            // CardMovedEvent fires, so the trigger must be active in both zones.
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
    }

    /// <summary>
    /// Apply the haste grant (CR 702.152b, first rider) to a blitz creature
    /// that just entered the battlefield, plus register the delayed end-step
    /// self-sacrifice (third rider) with <paramref name="triggers"/>. No-ops
    /// cleanly when <see cref="Creature.BlitzWasPaid"/> is false (the creature
    /// was cast for its normal mana cost / returned some other way).
    ///
    /// Called from the creature's ETB path. CR 603.7 — the delayed sacrifice is
    /// a one-shot delayed triggered ability that fires on the FIRST
    /// <see cref="StepStartedEvent"/> for <see cref="StepStateType.End"/>
    /// strictly after the creature entered (activation-time fence mirrors
    /// Through the Breach). CR 701.16 — sacrifice moves it from the
    /// controller's battlefield to its owner's graveyard.
    /// </summary>
    /// <param name="creature">The creature that just entered the battlefield.</param>
    /// <param name="triggers">Trigger manager the delayed sacrifice registers
    /// with. May be null — the haste grant still applies but the creature won't
    /// be sacrificed automatically (shape-only path).</param>
    /// <param name="zoneService">Zone service the sacrifice routes through so
    /// LTB / dies events (including the blitz dies-draw trigger above) fire.
    /// May be null — raw-zone sacrifice path.</param>
    public static void ApplyEntersRiders(
        Creature creature,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        if (creature == null) throw new ArgumentNullException(nameof(creature));
        if (!creature.BlitzWasPaid) return;

        // ---- Rider 1: gains haste (CR 702.10 / CR 702.152b). ----
        // Haste lifts summoning sickness for attack declaration (CR 702.10b);
        // clear the flag so the creature is attack-ready immediately. The
        // KeywordAbility marker carried on the creature (attached in the
        // factory) is the static "has haste" source the combat path reads.
        creature.HasSummoningSickness = false;

        if (triggers == null) return;

        // ---- Rider 3: sacrifice at the beginning of the next end step. ----
        // CR 603.7 — one-shot delayed triggered ability.
        var resolvedAt = Majik.Core.Game.LogicalClockScope.Current.NextTimestamp();
        var sacEffect = new Effect(
            $"Blitz — sacrifice {creature.Name} at the beginning of the next end step (CR 702.152b)",
            () =>
            {
                if (creature.Zone != ZoneType.Battlefield) return;
                var battlefield = creature.Controller?.Zones.Battlefield;
                if (battlefield == null) return;
                if (!battlefield.GetCards().Contains(creature)) return;

                // CR 701.16 — sacrifice: controller's battlefield → owner's
                // graveyard. ZoneService routes the publish (so the dies-draw
                // trigger fires) when supplied.
                var bfPlayer = creature.Controller!;
                var graveyardOwner = creature.Owner ?? bfPlayer;
                if (zoneService != null)
                {
                    zoneService.MoveCard(
                        creature, ZoneType.Battlefield, ZoneType.Graveyard, bfPlayer);
                }
                else
                {
                    bfPlayer.Zones.Battlefield.RemoveCard(creature);
                    graveyardOwner.Zones.Graveyard.AddCard(creature);
                    creature.SetZone(ZoneType.Graveyard);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: creature,
            controller: creature.Controller ?? creature.Owner
                ?? throw new InvalidOperationException("Blitz source must have a controller or owner"),
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == StepStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { sacEffect });

        triggers.RegisterDelayed(delayed);
    }
}
