using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.86 — Annihilator. "Annihilator N" means "Whenever this
/// creature attacks, defending player sacrifices N permanents."
/// CR 702.86b — multiple instances trigger separately.
///
/// Built atop the existing trigger framework as a parameterised
/// <see cref="TriggeredAbility"/> factory; the per-attacker trigger
/// fires on <see cref="CreatureAttacksEvent"/> (CR 508.1f), captures
/// the defending player off the live event, and routes the sacrifice
/// prompt through <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/>
/// — same agent-prompt shape as Liliana of the Veil's discard pick
/// (<see cref="IPlayerAgent.ChooseFromHandAsync"/>).
///
/// Sacrifices route through <see cref="Fx.Sacrifice"/> which calls
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
/// <see cref="ZoneMoveReason.Sacrifice"/> — CR 701.16 / 702.12b /
/// 701.15c: sacrifice bypasses Indestructible and regeneration.
/// Tokens cease to exist as a state-based action after hitting the
/// graveyard (CR 110.7 / 704.5d).
///
/// ## Wiring
/// <see cref="Build(Creature, int, Func{Player, IPlayerAgent?}?)"/> is
/// the production entry point — pass the source creature, the
/// annihilator value <em>N</em>, and an optional per-player agent
/// selector. When the selector is null (or returns null for the
/// defender), the deterministic first-N-permanents fallback applies
/// (legacy pre-agent posture).
///
/// Cards that print Annihilator N should also stamp a
/// <c>KeywordAbility("Annihilator", source, controller, arg: N)</c>
/// marker on the card for discoverability (CombatAbilities-style
/// keyword scan). The factory consumes the marker via its constructor
/// parameter, not by re-reading the marker, so the marker is purely
/// observability.
/// </summary>
public static class AnnihilatorFactory
{
    /// <summary>
    /// Build the Annihilator trigger for <paramref name="source"/> with
    /// value <paramref name="n"/>. Active in <see cref="ZoneType.Battlefield"/>
    /// only (CR 702.86a — triggered ability on a creature).
    /// </summary>
    /// <param name="source">The Annihilator-carrying creature. Must have
    /// a controller set; the trigger's controller is the source's
    /// controller at trigger time.</param>
    /// <param name="n">The Annihilator value (1 or more). N ≤ 0 builds
    /// a structural no-op trigger — defending player chooses zero
    /// permanents to sacrifice.</param>
    /// <param name="agentSelector">Optional per-player agent lookup. When
    /// supplied, the defending player's agent is consulted via
    /// <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> for each of
    /// the N sacrifice picks (<see cref="Cards.BotIntent.Removal"/>).
    /// Null falls back to deterministic first-N-permanents (legacy
    /// pre-agent behaviour).</param>
    public static TriggeredAbility Build(
        Creature source,
        int n,
        Func<Player, IPlayerAgent?>? agentSelector = null)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (source.Controller == null)
            throw new InvalidOperationException("Annihilator source must have a controller");

        // Capture defender across condition → effect (mirrors Ulamog's
        // capturedDefender closure pattern — the event is the
        // single source of truth for "defending player at attack time"
        // CR 506.2 / 506.3).
        Player? capturedDefender = null;

        var condition = new EventTriggerCondition<CreatureAttacksEvent>(
            (e, _) =>
            {
                if (!ReferenceEquals(e.Attacker, source)) return false;
                // CR 506.2 — defender at attack time. Planeswalker
                // defender has no permanents to sacrifice as a
                // "defending player" — but CR 802.1 says the attack
                // also has a defending player (the planeswalker's
                // controller). For v1 we only fire when the event's
                // top-level defender slot is a Player; planeswalker
                // defenders trigger separately when the engine
                // exposes the controller-of-defending-planeswalker
                // through the event.
                capturedDefender = e.DefendingPlayerOrPlaneswalker switch
                {
                    Player p => p,
                    Majik.Core.Cards.Planeswalker pw => pw.Controller,
                    _ => null,
                };
                return capturedDefender != null;
            });

        var effect = new Effect(
            $"Annihilator {n}: defending player sacrifices {n} permanent{(n == 1 ? "" : "s")}",
            () =>
            {
                var victim = capturedDefender;
                if (victim == null) return;
                if (n <= 0) return;

                var sacrificed = 0;
                while (sacrificed < n)
                {
                    // Re-read the battlefield each iteration — the
                    // previous sacrifice may have removed multiple
                    // permanents (LTB triggers, etc.) so a snapshot
                    // taken once would race.
                    var candidates = victim.Zones.Battlefield
                        .GetCards()
                        .ToList();
                    if (candidates.Count == 0) break;

                    ICard? pick;
                    var agent = agentSelector?.Invoke(victim);
                    if (agent != null)
                    {
                        pick = agent.ChooseFromBattlefieldAsync(
                                victim,
                                candidates,
                                Cards.BotIntent.Removal)
                            .GetAwaiter().GetResult();
                        // CR 608.2b — illegal-on-resolution check. If
                        // the agent returns something not on the
                        // defender's battlefield anymore (or null),
                        // fall back to the first candidate.
                        if (pick == null
                            || pick.Zone != ZoneType.Battlefield
                            || !ReferenceEquals(pick.Controller, victim))
                        {
                            pick = candidates[0];
                        }
                    }
                    else
                    {
                        // Deterministic v1 fallback — first permanent.
                        pick = candidates[0];
                    }

                    // CR 701.16 / 702.12b / 701.15c — sacrifice
                    // bypasses Indestructible + regeneration. CR 110.7
                    // — token in graveyard ceases to exist next SBA
                    // pass. Fx.Sacrifice routes through the binder's
                    // reason-gated MoveToGraveyard.
                    Fx.Sacrifice(pick);
                    sacrificed++;
                }
            });

        return new TriggeredAbility(
            source: source,
            controller: source.Controller,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });
    }
}
