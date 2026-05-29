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
/// Named-card factory for Arc Trail (Scars of Mirrodin, {1}{R}).
///
/// Sorcery. Oracle text (Scryfall, verified):
///   "Arc Trail deals 2 damage to any target and 1 damage to any other
///    target."
///
/// ## Shape
/// Two-target fixed-split burn — the same structural pattern as
/// <see cref="SearingBlazeFactory"/> (two simultaneous 1..1 target
/// requests, each request taking its own fixed damage amount on
/// resolution). Unlike <see cref="ForkedBoltFactory"/>, the damage is
/// <em>not</em> divided as the caster chooses: it is fixed per clause
/// (2 to the first target, 1 to the second), so no division prompt is
/// needed.
///
/// Both clauses are "any target" (CR 115.3 — creature, player,
/// planeswalker, or battle). Damage is routed through
/// <see cref="Fx.DealDamageAny"/> so all legal target classes resolve
/// correctly: Player → life loss (CR 120.3), Creature → marked damage
/// (CR 119.3), Planeswalker → loyalty removal (CR 306.7).
///
/// ## "any other target" (CR 601.2c)
/// The second clause's target must be different from the first. The
/// engine's target-request declaration cannot yet express a
/// "different-from-target[0]" constraint at the <see cref="TargetRequest"/>
/// level, so — same V1 posture as <see cref="SearingBlazeFactory"/>'s
/// "controlled by the previous target's controller" relationship — the
/// distinctness constraint is enforced at the agent/caller level, not in
/// the factory. The resolve effect honours both target picks and deals
/// the per-clause damage to each. If a caller picks the same object
/// twice it takes both (2 + 1 = 3); a correct caller never does.
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{R}.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares two 1..1 "any target"
///   requests; on resolution target[0] takes <see cref="MajorDamage"/>
///   (2) and target[1] takes <see cref="MinorDamage"/> (1).
///
/// ## Deferred (v1 gaps)
/// - <b>Distinct-target enforcement at declaration</b>: "any other
///   target" is caller-enforced rather than expressed on the second
///   <see cref="TargetRequest"/> — matches Searing Blaze's relaxed
///   relationship posture.
/// - <b>Damage prevention / replacement (CR 615)</b>: damage flows
///   straight through <see cref="Fx.DealDamageAny"/>, same as
///   <see cref="ShockFactory"/> / Forked Bolt.
/// </summary>
[CardName("Arc Trail")]
public static class ArcTrailFactory
{
    public const string CardName = "Arc Trail";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>Damage dealt to the first ("any target") clause.</summary>
    public const int MajorDamage = 2;

    /// <summary>Damage dealt to the second ("any other target") clause.</summary>
    public const int MinorDamage = 1;

    /// <summary>CardDef DSL — card shape only. The twin-target damage body
    /// is supplied at cast time via <see cref="BuildSpellDefinition"/> (the
    /// runtime needs the caller's target resolver, which lives on the
    /// <see cref="GameContext"/>).</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Arc Trail is cast.
    /// Two 1..1 "any target" requests; on resolution target[0] takes
    /// <see cref="MajorDamage"/> (2) and target[1] takes
    /// <see cref="MinorDamage"/> (1), both via
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
                new TargetRequest(
                    Description: "any target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
                // "any other target" — distinctness from target[0] is
                // caller-enforced (V1), see class remarks.
                new TargetRequest(
                    Description: "any other target",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var major = resolver(chosen.Targets[0][0]);
                var minor = resolver(chosen.Targets[1][0]);
                return new IEffect[]
                {
                    Fx.Inline(
                        $"{CardName}: {MajorDamage} damage to any target and {MinorDamage} to any other target",
                        () =>
                        {
                            Fx.DealDamageAny(major, MajorDamage);
                            Fx.DealDamageAny(minor, MinorDamage);
                        }),
                };
            });
    }
}
