using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fatal Push (Aether Revolt / various reprints, {B}).
///
/// Instant. Oracle text:
///   "Destroy target creature if it has mana value 2 or less.
///    Revolt — Destroy that creature if it has mana value 4 or less
///    instead if a permanent left the battlefield under your control this
///    turn."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}, black.
/// - Resolve via <see cref="BuildSpellDefinition"/>: single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution:
///   1. Target must still be a Creature on the Battlefield (CR 608.2b —
///      illegal-target filter at resolve, no-op otherwise).
///   2. Sample Revolt (CR 702.104a) for the spell controller from
///      <see cref="TurnState.RevoltActive(Player)"/> — at least one
///      permanent the controller controlled left the battlefield this
///      turn. When no <see cref="TurnState"/> is wired (shape / dispatch
///      tests), Revolt is treated as inactive (base clause applies).
///   3. Threshold = Revolt ? 4 : 2 on the target's
///      <see cref="Card.ManaCostValue"/> total. Note: this matches
///      <see cref="SpellTemplates.Templates.Destroy.DestroyCreatureCmcLimitTemplate"/>'s
///      use of <c>ManaCostValue.TotalValue</c> rather than a CDA-aware
///      <c>ManaValue</c> (Rule 202.3 — mana value is computed from the
///      printed mana cost; X spells in zones other than the stack have
///      X = 0). MDFC back-face / split-card lookups are not normalized
///      here — same posture as the shared template.
///   4. When the target's mana value ≤ threshold, destroy via
///      <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///      <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///      (CR 702.12) and regeneration shields (CR 701.15) are honoured.
///
/// Coverage note: the data-driven
/// <see cref="SpellTemplates.Templates.Destroy.DestroyCreatureCmcLimitTemplate"/>
/// matches Fatal Push's <i>first sentence only</i> ("Destroy target creature
/// if it has mana value 2 or less") — the Revolt clause is dropped by that
/// template. This named factory exists specifically to wire the Revolt
/// upgrade so production casts of Fatal Push see the full printed effect
/// (and the bot's removal range expands appropriately when Revolt is live).
///
/// ## Deferred
/// - Mana-value normalization via a CDA-aware <c>ManaValue</c> helper
///   (split / adventure / MDFC back face). Out of scope until the engine
///   exposes a unified accessor — matches the rest of the destroy-cmc-limit
///   surface.
/// </summary>
[CardName("Fatal Push")]
public static class FatalPushFactory
{
    public const string CardName = "Fatal Push";
    public const string PrintedManaCost = "{B}";

    /// <summary>Base "destroy if mana value ≤ N" threshold.</summary>
    public const int BaseManaValueLimit = 2;

    /// <summary>Revolt-upgraded "destroy if mana value ≤ N" threshold.</summary>
    public const int RevoltManaValueLimit = 4;

    /// <summary>CardDef DSL — card shape only. Revolt-gated destroy body
    /// lives in <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Fatal Push is
    /// cast. Single 1..1 target creature; on resolution the controller's
    /// per-turn permanent-left tally is sampled (Revolt — CR 702.104) and
    /// the mana-value threshold raised from 2 → 4 when active.
    /// </summary>
    /// <param name="controller">Spell controller — whose Revolt tally
    /// drives the conditional upgrade.</param>
    /// <param name="turnStateResolver">Callback returning the live
    /// <see cref="TurnState"/> at resolution time. Null return (no driver
    /// wired — typical for shape / dispatcher tests) is treated as Revolt
    /// inactive (base threshold applies). Same posture as
    /// <see cref="SearingBlazeFactory.IsLandfallActive"/> /
    /// <see cref="ForceOfDespairFactory.BuildSpellDefinition"/>.</param>
    /// <param name="targetResolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<TurnState?> turnStateResolver,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature if its mana value is at most {BaseManaValueLimit} ({RevoltManaValueLimit} with Revolt active)",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 702.104a — Revolt is active iff a
                            // permanent the spell controller controlled
                            // left the battlefield this turn.
                            var threshold = IsRevoltActive(controller, turnStateResolver)
                                ? RevoltManaValueLimit
                                : BaseManaValueLimit;

                            // Rule 202.3 — mana value of the target's
                            // printed cost. Matches DestroyCreatureCmcLimit-
                            // Template's accessor.
                            var manaValue = target.ManaCostValue.TotalValue;
                            if (manaValue > threshold) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }

    /// <summary>
    /// Sample <paramref name="controller"/>'s per-turn permanent-left tally
    /// (CR 702.104a). True iff at least one permanent the controller
    /// controlled left the battlefield this turn. Returns false when no
    /// <see cref="TurnState"/> is wired (typical for shape / dispatcher
    /// tests) — same posture as
    /// <see cref="SearingBlazeFactory.IsLandfallActive"/>.
    /// </summary>
    public static bool IsRevoltActive(
        Player controller,
        Func<TurnState?> turnStateResolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(turnStateResolver);
        var turnState = turnStateResolver.Invoke();
        return turnState != null && turnState.RevoltActive(controller);
    }
}
