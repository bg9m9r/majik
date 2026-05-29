using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Skred (Coldsnap, {R}).
///
/// Instant. Oracle text:
///   "Skred deals damage to target creature equal to the number of snow
///    permanents you control."
///
/// ## Implemented (v1)
/// - Instant card shape ({R}, Red) — built via the fluent
///   <see cref="CardDef"/> DSL, same shape as <see cref="DefileFactory"/>.
/// - 1..1 "target creature" <see cref="TargetRequest"/> (Intent: Removal),
///   mirrors <see cref="DefileFactory.BuildSpellDefinition"/>'s target shape.
/// - Resolve effect (<see cref="BuildSpellDefinition"/>):
///   1. <c>N</c> = number of snow permanents the caster controls — scans the
///      controller's battlefield for permanents carrying the
///      <see cref="CardSupertype.Snow"/> supertype (CR 205.4d — Snow is a
///      supertype; "snow permanent" is any permanent with that supertype,
///      regardless of card type, so snow lands, snow creatures, snow
///      artifacts, etc. all count).
///   2. Deals <c>N</c> damage to the chosen creature via
///      <see cref="Fx.DealDamage"/> (CR 119 — damage is "marked" on the
///      creature; lethal damage triggers SBA CR 704.5g at the next check).
/// - <c>N == 0</c> (no snow permanents) collapses to a clean no-op: 0 damage
///   is a no-op via <see cref="Fx.DealDamage"/>'s amount-≤-0 guard.
///
/// ## Notes
/// - Unlike <see cref="DefileFactory"/> (which counts only the
///   <see cref="CardSubtype.Swamp"/> land subtype), Skred counts the Snow
///   <em>supertype</em> across ALL permanent types — this is the key
///   difference between the two cards and is reflected in
///   <see cref="CountSnowPermanents"/>.
/// - Skred itself is on the stack while resolving (CR 608.2 — it is a spell,
///   not a permanent) so it never counts toward its own total.
/// </summary>
[CardName("Skred")]
public static class SkredFactory
{
    public const string CardName = "Skred";
    public const string PrintedManaCost = "{R}";

    /// <summary>CardDef DSL — card shape only. The N-damage body lives in
    /// <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Count how many snow permanents <paramref name="controller"/> currently
    /// controls (CR 205.4d — Snow supertype; any permanent type counts).
    /// Exposed for bot policies + tests that want to sample the value without
    /// resolving the full spell. Returns 0 for null input.
    /// </summary>
    public static int CountSnowPermanents(Player controller)
    {
        if (controller == null) return 0;
        return controller.Zones.Battlefield.GetCards()
            .OfType<Card>()
            .Count(c => c.HasSupertype(CardSupertype.Snow));
    }

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Mirrors
    /// <see cref="DefileFactory.BuildSpellDefinition"/>'s shape modulo the
    /// snow-count read and the damage-only body (no -X/-X pump).
    /// </summary>
    /// <param name="caster">Spell controller — whose snow-permanent count is
    /// read at resolution.</param>
    /// <param name="targetResolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
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
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
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
                        $"{CardName} — N damage where N = snow permanents controller controls",
                        () => Resolve(caster, resolved)),
                };
            });
    }

    private static void Resolve(Player caster, object target)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (target is not Creature creature) return;
        if (creature.Zone != ZoneType.Battlefield) return;

        var n = CountSnowPermanents(caster);
        if (n <= 0) return; // no snow permanents → 0 damage = clean no-op

        // CR 119 — N damage marked on the creature; lethal SBA at the next
        // SBA check (CR 704.5g).
        Fx.DealDamage(creature, n);
    }
}
