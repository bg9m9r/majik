using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Infuriate (Ixalan, {R}).
///
/// Instant. Oracle text:
///   "Target creature gets +3/+2 until end of turn."
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {R}.
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1..1 "target creature" <see cref="TargetRequest"/>. On resolve:
///   register a <see cref="PumpUntilEndOfTurnEffect"/>(+3, +2) on the
///   target creature's <see cref="Creature.ActiveEffects"/> (CR 514.2 —
///   "until end of turn"). CR 608.2b: if the target is no longer a
///   Creature on the battlefield at resolution, the effect no-ops.
///
/// Mirrors <see cref="GiantGrowthFactory"/> with asymmetric +3/+2 pump
/// at {R} instead of +3/+3 at {G}.
/// </summary>
[CardName("Infuriate")]
public static class InfuriateFactory
{
    public const string CardName = "Infuriate";
    public const string PrintedManaCost = "{R}";

    /// <summary>Layer 7c power boost magnitude (CR 613.1g).</summary>
    public const int PowerBoost = 3;

    /// <summary>Layer 7c toughness boost magnitude (CR 613.1g).</summary>
    public const int ToughnessBoost = 2;

    /// <summary>CardDef DSL — card shape only. The pump SpellDefinition
    /// lives in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "target creature gets +3/+2 until end of turn"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a <see cref="Creature"/>
    /// on the Battlefield (CR 608.2b — illegal target → no-op). When
    /// valid, registers a <see cref="PumpUntilEndOfTurnEffect"/>(+3, +2)
    /// on the target's <see cref="Creature.ActiveEffects"/> (CR 514.2 —
    /// expires in cleanup). When ActiveEffects is null (shape-only
    /// tests without a live <see cref="ContinuousEffectsService"/>), the
    /// registration is a no-op.
    /// </summary>
    public static SpellDefinition BuildDefinition() =>
        new(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Infuriate — target creature gets +3/+2 until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;
        if (target.ActiveEffects == null) return;

        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PowerBoost, ToughnessBoost));
    }
}
