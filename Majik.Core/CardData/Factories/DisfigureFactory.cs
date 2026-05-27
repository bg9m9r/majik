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
/// Named-card factory for Disfigure (Worldwake / various reprints, {B}).
///
/// Instant. Oracle text:
///   "Target creature gets -2/-2 until end of turn."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}, owner / controller.
/// - <b>-2/-2 until end of turn</b> — <see cref="BuildDefinition"/> wires
///   the resolve effect: a single 1..1 "target creature"
///   <see cref="TargetRequest"/>. On resolve: register a
///   <see cref="PumpUntilEndOfTurnEffect"/>(-2, -2) on the target
///   creature's <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires
///   at EOT). Same pattern as <see cref="DismemberFactory"/>.
///   CR 608.2b: target not on battlefield → no-op. When ActiveEffects is
///   null (shape-only tests without a live ContinuousEffectsService) the
///   registration is a no-op.
/// </summary>
[CardName("Disfigure")]
public static class DisfigureFactory
{
    public const string CardName = "Disfigure";
    public const string PrintedManaCost = "{B}";

    /// <summary>CardDef DSL — card shape only. The -2/-2 pump
    /// SpellDefinition lives in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "target creature gets -2/-2 until end of turn"
    /// SpellDefinition.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// (CR 608.2b — illegal target → no-op). When valid, registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(-2, -2) on the target's
    /// <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at EOT).
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
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: enumerate every creature live; the
                    // bot ranks opponent's biggest small threat via Removal
                    // intent.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                return new IEffect[]
                {
                    new Effect(
                        "Disfigure — target creature gets -2/-2 until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // Register a -2/-2 EOT-scoped Layer 7c effect on the target creature.
        // Same pattern as DismemberFactory / TheMeathookMassacreFactory.
        // When ActiveEffects is null (shape tests without a live
        // ContinuousEffectsService), the effect registration is a no-op.
        if (target.ActiveEffects == null) return;
        target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, -2, -2));
    }
}
