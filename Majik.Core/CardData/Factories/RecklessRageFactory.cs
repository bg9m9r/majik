using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reckless Rage (Rivals of Ixalan, {R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Reckless Rage deals 4 damage to target creature you don't control
///    and 2 damage to target creature you control."
///
/// ## Shape
/// Two-target fixed-split burn — the same structural pattern as
/// <see cref="ArcTrailFactory"/> (two simultaneous 1..1 target requests,
/// each request taking its own fixed damage amount on resolution, both
/// routed through <see cref="Fx.DealDamageAny"/>). Unlike Arc Trail,
/// the two targets are <em>creatures only</em> (CR 115.4) and each is
/// constrained by its controller relative to the caster (CR 601.2c):
///   - request[0] — "target creature you don't control" → 4 damage.
///   - request[1] — "target creature you control"       → 2 damage.
///
/// ## Controller-constrained targeting (CR 601.2c)
/// Rather than relaxing the controller restriction to a caller-enforced
/// invariant (the Arc Trail "any other target" posture), each
/// <see cref="TargetRequest"/> carries a per-request
/// <see cref="TargetRequest.CandidateGatherer"/> keyed off
/// <see cref="GameContext.Self"/> (the caster): the "you don't control"
/// request gathers only battlefield creatures whose controller is not the
/// caster, and the "you control" request gathers only creatures the caster
/// controls. This expresses the constraint in the candidate pool itself —
/// the same live-gatherer mechanism <see cref="FellFactory"/> uses — so a
/// bot or agent is never offered an illegal target.
///
/// Card shape comes from the embedded JSON (<c>reckless-rage.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/>. The resolve-time body lives in
/// <see cref="BuildSpellDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's <see cref="GameContext"/>
/// (not expressible in the data-only JSON schema).
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {R}.
/// - Resolve-time <see cref="SpellDefinition"/> declares two 1..1
///   controller-partitioned "target creature" requests; on resolution
///   target[0] takes <see cref="OpponentDamage"/> (4) and target[1] takes
///   <see cref="SelfDamage"/> (2), both via <see cref="Fx.DealDamageAny"/>
///   (creature → marked damage, CR 119.3).
/// - CR 608.2b illegal-target guard: the resolve body only deals damage to
///   targets that are still creatures at resolution (a non-creature live
///   object is skipped).
///
/// ## Deferred (v1 gaps)
/// - <b>Damage prevention / replacement (CR 615)</b>: damage flows straight
///   through <see cref="Fx.DealDamageAny"/>, same posture as
///   <see cref="ArcTrailFactory"/> / <see cref="ShockFactory"/>.
/// </summary>
[CardName("Reckless Rage")]
public static class RecklessRageFactory
{
    public const string CardName = "Reckless Rage";
    public const string Slug = "reckless-rage";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 — damage to the creature the caster does NOT control.</summary>
    public const int OpponentDamage = 4;

    /// <summary>CR 119 — damage to the creature the caster controls.</summary>
    public const int SelfDamage = 2;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Reckless Rage is
    /// cast. Two 1..1 controller-partitioned "target creature" requests; on
    /// resolution target[0] (a creature the caster doesn't control) takes
    /// <see cref="OpponentDamage"/> (4) and target[1] (a creature the caster
    /// controls) takes <see cref="SelfDamage"/> (2), both via
    /// <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target token → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                // CR 601.2c — "target creature you don't control": only
                // battlefield creatures the caster does not control.
                new TargetRequest(
                    Description: "target creature you don't control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal | BotIntent.Burn,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !ReferenceEquals(c.Controller, ctx.Self))
                        .Cast<object>()
                        .ToList()),
                // CR 601.2c — "target creature you control": only battlefield
                // creatures the caster controls.
                new TargetRequest(
                    Description: "target creature you control",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Burn,
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => ReferenceEquals(c.Controller, ctx.Self))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var opponentTarget = resolver(chosen.Targets[0][0]);
                var ownTarget = resolver(chosen.Targets[1][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: {OpponentDamage} damage to a creature you don't control and {SelfDamage} to a creature you control",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality: only deal
                            // damage to targets that are still creatures.
                            if (opponentTarget is Creature) Fx.DealDamageAny(opponentTarget, OpponentDamage);
                            if (ownTarget is Creature) Fx.DealDamageAny(ownTarget, SelfDamage);
                        }),
                };
            });
    }
}
