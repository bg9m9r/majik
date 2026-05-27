using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sip of Hemlock (Onslaught, {4}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Destroy target creature. Its controller loses 2 life."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {4}{B}{B}, owner / controller.
/// - <b>Destroy target creature</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution
///   the chosen creature is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) iff it is still a Creature on the Battlefield (CR 608.2b —
///   illegal target at resolution → entire spell does nothing).
/// - <b>Its controller loses 2 life</b> — the creature's controller is
///   captured BEFORE the destroy (the permanent may leave the battlefield
///   after the zone move, but the controller reference is stable).
///   The 2 life loss is applied to the captured controller as part of the
///   same resolution effect, gated on the same legality check (CR 608.2b).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate.
/// Unlike Vendetta there is no colour restriction — Sip of Hemlock destroys
/// any creature regardless of colour (CR 105).
/// </summary>
[CardName("Sip of Hemlock")]
public static class SipOfHemlockFactory
{
    public const string CardName = "Sip of Hemlock";
    public const string PrintedManaCost = "{4}{B}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target creature; its controller loses 2 life) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target creature; its controller loses 2 life"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// (CR 608.2b — illegal-target filter at resolution). When valid:
    ///   1. Captures the target's controller BEFORE destruction.
    ///   2. Destroys the target via
    ///      <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    ///      <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/>
    ///      (CR 701.7) — indestructible / regeneration handled at the
    ///      destroy site.
    ///   3. Applies 2 life loss to the captured controller (CR 119.3).
    ///
    /// No colour restriction — any creature is a legal target (CR 105).
    /// </summary>
    /// <param name="caster">Spell controller (unused in effect; included for
    /// API symmetry with analogous factories).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
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
                    Intent: BotIntent.Removal,
                    // Gather every creature on any battlefield.
                    // No colour restriction — Sip of Hemlock is unconditional
                    // removal. Removal intent in the bot's ranker pushes the
                    // opponent's biggest threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature; its controller loses 2 life",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // If the target is illegal the entire spell does
                            // nothing — including the 2 life-loss rider.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Capture the controller BEFORE destruction — the
                            // permanent will leave the battlefield after the
                            // zone move but the Player reference remains valid.
                            var controller = target.Controller;
                            if (controller is null) return; // safety: uncontrolled permanent — no life loss

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);

                            // Oracle text: "Its controller loses 2 life."
                            // Applied to the creature's controller (captured
                            // above), not the caster (CR 119.3).
                            controller.LoseLife(2);
                        }),
                };
            });
    }
}
