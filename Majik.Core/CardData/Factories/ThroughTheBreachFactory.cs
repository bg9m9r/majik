using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Through the Breach (Champions of Kamigawa, {2}{R}{R}).
///
/// Instant. Oracle text:
///   "You may put a creature card from your hand onto the battlefield. That
///    creature gains haste until end of turn. Sacrifice that creature at the
///    beginning of the next end step.
///    Splice onto Arcane {1}{R}{R}{R}."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{R}{R}.
/// - Card shape only on <see cref="Create(Player)"/>. The resolve effect is
///   built on demand via <see cref="BuildResolveEffect"/> so tests /
///   integrations can splice it into a <see cref="Majik.Core.Game.SpellDefinition"/>
///   or pass it directly to a <see cref="Majik.Core.Spells.Spell"/>.
/// - Resolve effect:
///   1. Picks a creature card from the caster's hand (v1 deterministic
///      first-creature pick — same "you may" auto-accept shape as Aether
///      Vial's tap activation). If no creature is in hand the effect is a
///      clean no-op (CR 117.x — "you may" with no valid target).
///   2. Moves the picked creature from hand to the battlefield. Routes
///      through <see cref="ZoneService.MoveCard"/> when supplied so ETB
///      triggers on the placed creature fire (CR 603.6a). Falls back to
///      raw zone manipulation otherwise (shape-only path).
///   3. Grants Haste until end of turn via
///      <see cref="GrantKeywordUntilEndOfTurnEffect"/> on the placed
///      creature's <see cref="Creature.ActiveEffects"/> when one is
///      attached (CR 613.1c Layer 6 / CR 702.10).
///   4. Registers a one-shot <see cref="DelayedTriggeredAbility"/>
///      (CR 603.7) on the supplied <see cref="TriggerManager"/> that
///      sacrifices the placed creature at the start of the next end step
///      (CR 500.4 / CR 701.16 — controller's battlefield → owner's
///      graveyard). The trigger fence-checks <c>e.Timestamp &gt; resolvedAt</c>
///      so the current end step (if any) doesn't trip it (mirrors the
///      activation-time fence used by Mishra's Bauble / Wrenn's Resolve /
///      Splinter Twin).
///
/// ## Deferred (v1 gaps)
/// - <b>Splice onto Arcane (CR 702.46)</b>: the splice alt-cost
///   primitive isn't in the engine yet. Through the Breach is still
///   castable for its printed cost; the splice rider is structural-only
///   on the oracle text and will be added when the engine has an Arcane-
///   spell awareness pass (same gap as every other Splice card).
/// - <b>"You may" prompt</b>: defaults to taking the action when an
///   eligible creature exists in hand (matches Aether Vial / Dredger's
///   Insight v1 prompt behavior). Real agent-driven yes/no + creature-pick
///   awaits the prompt MVP.
/// - <b>Empty-hand / no-creature-in-hand</b>: clean no-op. The spell
///   still resolves; no creature is put onto the battlefield and no
///   delayed trigger is registered (there is no creature to sacrifice).
/// - <b>ActiveEffects on placed creature</b>: if the picked creature has
///   no <see cref="Creature.ActiveEffects"/> wired (test/shape mode),
///   the Haste grant is skipped silently. Production callers wire a
///   <see cref="ContinuousEffectsService"/> on creatures before they
///   move to the battlefield (same shape as Reckless Charge's pump path).
/// </summary>
[CardName("Through the Breach")]
public static class ThroughTheBreachFactory
{
    public const string CardName = "Through the Breach";
    public const string PrintedManaCost = "{2}{R}{R}";

    /// <summary>Granted keyword. CR 702.10 — Haste.</summary>
    public const string GrantedKeyword = "Haste";

    /// <summary>
    /// Build a Through the Breach instant owned by <paramref name="owner"/>.
    /// Card shape only — see <see cref="BuildResolveEffect"/> for the
    /// resolve-time put-from-hand + haste-grant + delayed sac.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Through the Breach's resolve effect. On resolution, deterministically
    /// picks the first creature card in <paramref name="caster"/>'s hand and:
    /// moves it to the battlefield (via <paramref name="zoneService"/> when
    /// supplied), grants Haste until end of turn, and (when
    /// <paramref name="triggers"/> is supplied) registers a delayed end-step
    /// sacrifice trigger.
    /// </summary>
    /// <param name="caster">The spell's controller / creature-source owner.</param>
    /// <param name="zoneService">Optional. When supplied the hand →
    /// battlefield move routes through <see cref="ZoneService.MoveCard"/>
    /// so ETB triggers on the placed creature fire (CR 603.6a). Shape-only
    /// callers can pass null — the move falls back to raw zone manipulation.</param>
    /// <param name="triggers">Optional. When supplied the delayed
    /// end-step sacrifice trigger is registered with the trigger manager.
    /// Shape-only callers can pass null — the put-from-hand + haste grant
    /// still happen, but the creature won't be sacrificed automatically.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        ZoneService? zoneService = null,
        TriggerManager? triggers = null,
        IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                "Through the Breach: put creature from hand → battlefield, haste EOT, sac next end step.",
                () => ResolveBody(caster, zoneService, triggers, agent)),
        };
    }

    /// <summary>
    /// Picks the first creature card in <paramref name="caster"/>'s hand,
    /// moves it to the battlefield, grants Haste until end of turn, and
    /// (when <paramref name="triggers"/> is supplied) registers a delayed
    /// end-step sacrifice for that specific creature instance.
    /// No-ops cleanly when no creature is in hand.
    /// </summary>
    private static void ResolveBody(
        Player caster,
        ZoneService? zoneService,
        TriggerManager? triggers,
        IPlayerAgent? agent)
    {
        // -------------------------------------------------------------------
        // "You may put a creature card from your hand onto the battlefield."
        // With an agent supplied: ChooseYesNoAsync(CheatIntoPlay) +
        // ChooseFromHandAsync. Without: deterministic v1 first-creature
        // pick + auto-accept. No creature in hand → no-op.
        // -------------------------------------------------------------------
        var creatures = caster.Zones.Hand.GetCards()
            .OfType<Creature>()
            .Cast<ICard>()
            .ToList();
        if (creatures.Count == 0) return;

        Creature? pick;
        if (agent != null)
        {
            var yes = agent.ChooseYesNoAsync(
                "Put a creature card from your hand onto the battlefield?",
                BotIntent.CheatIntoPlay).GetAwaiter().GetResult();
            if (!yes) return;
            var chosen = agent.ChooseFromHandAsync(
                caster, creatures, BotIntent.CheatIntoPlay)
                .GetAwaiter().GetResult();
            if (chosen is not Creature c) return;
            if (c.Zone != ZoneType.Hand) return;
            pick = c;
        }
        else
        {
            pick = (Creature)creatures[0];
        }

        // -------------------------------------------------------------------
        // Hand → Battlefield. Routes through ZoneService when supplied so
        // CardMovedEvent publishes (ETB triggers on the placed creature
        // fire — CR 603.6a). Raw zone manipulation is the shape-only path
        // (mirrors AetherVial's PutCreatureFromHand fallback).
        // -------------------------------------------------------------------
        if (zoneService != null)
        {
            zoneService.MoveCard(pick, ZoneType.Hand, ZoneType.Battlefield, caster);
        }
        else
        {
            caster.Zones.Hand.RemoveCard(pick);
            caster.Zones.Battlefield.AddCard(pick);
            pick.SetZone(ZoneType.Battlefield);
            pick.SetController(caster);
        }

        // -------------------------------------------------------------------
        // "That creature gains haste until end of turn."
        // CR 613.1c (Layer 6) — keyword grant via the standard
        // GrantKeywordUntilEndOfTurnEffect that Reckless Charge / etc. use.
        // No-op silently when no ActiveEffects service is wired
        // (test/shape mode).
        // -------------------------------------------------------------------
        if (pick.ActiveEffects != null)
        {
            pick.ActiveEffects.Register(
                new GrantKeywordUntilEndOfTurnEffect(pick, GrantedKeyword));
        }
        // Haste lifts summoning sickness for attack-declaration (CR 702.10b);
        // clear the flag so the placed creature is attack-ready immediately
        // even when the live characteristics service isn't wired.
        pick.HasSummoningSickness = false;

        // -------------------------------------------------------------------
        // "Sacrifice that creature at the beginning of the next end step."
        // CR 603.7 — one-shot delayed triggered ability. Fires on the first
        // StepStartedEvent(End) strictly after this resolve (activation-time
        // fence mirrors Mishra's Bauble / Wrenn's Resolve / Splinter Twin).
        // Resolution moves the creature from controller's battlefield to
        // owner's graveyard (CR 701.16). Guards the zone check at fire
        // time so a creature that's already left the battlefield (bounce,
        // destroy, exile) doesn't get yanked from elsewhere.
        // -------------------------------------------------------------------
        if (triggers == null) return;

        var resolvedAt = DateTime.UtcNow;
        var sacEffect = new Effect(
            $"Through the Breach: sacrifice {pick.Name} at next end step",
            () =>
            {
                if (pick.Zone != ZoneType.Battlefield) return;
                var battlefield = pick.Controller?.Zones.Battlefield;
                if (battlefield == null) return;
                if (!battlefield.GetCards().Contains(pick)) return;

                // CR 701.16 — sacrifice: controller's battlefield → owner's
                // graveyard. ZoneService routes the publish when supplied.
                var bfPlayer = pick.Controller!;
                var graveyardOwner = pick.Owner ?? caster;
                if (zoneService != null)
                {
                    zoneService.MoveCard(
                        pick, ZoneType.Battlefield, ZoneType.Graveyard, bfPlayer);
                }
                else
                {
                    bfPlayer.Zones.Battlefield.RemoveCard(pick);
                    graveyardOwner.Zones.Graveyard.AddCard(pick);
                    pick.SetZone(ZoneType.Graveyard);
                }
            });

        var delayed = new DelayedTriggeredAbility(
            source: caster,
            controller: caster,
            condition: new EventTriggerCondition<StepStartedEvent>(
                (e, _) => e.StepType == PhaseStateType.End
                          && e.Timestamp > resolvedAt),
            effects: new IEffect[] { sacEffect });

        triggers.RegisterDelayed(delayed);
    }
}
