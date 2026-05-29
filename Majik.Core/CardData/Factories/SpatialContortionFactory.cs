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
/// Named-card factory for Spatial Contortion (Future Sight, {1}{C}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "({C} represents colorless mana.)
///    Target creature gets +3/-3 until end of turn."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{C} (one generic + one colorless), owner /
///   controller. Card shape comes from the embedded JSON
///   (<c>spatial-contortion.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>. The reminder text "({C} represents
///   colorless mana.)" is purely explanatory (CR 207.2 — reminder text has no
///   rules effect) so it shapes no behaviour.
/// - <b>+3/-3 until end of turn</b> — <see cref="BuildDefinition"/> wires the
///   resolve effect: a single 1..1 "target creature"
///   <see cref="TargetRequest"/>. On resolve: register a
///   <see cref="PumpUntilEndOfTurnEffect"/>(+3, -3) on the target creature's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 / CR 613 Layer 7c —
///   expires at end of turn). Same pattern as
///   <see cref="DisfigureFactory"/> / <see cref="GraspOfDarknessFactory"/>,
///   only with an asymmetric +3/-3 delta.
///   CR 608.2b: target not on battlefield → no-op. When ActiveEffects is null
///   (shape-only tests without a live ContinuousEffectsService) the
///   registration is a no-op.
/// </summary>
[CardName("Spatial Contortion")]
public static class SpatialContortionFactory
{
    public const string CardName = "Spatial Contortion";
    public const string Slug = "spatial-contortion";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "target creature gets +3/-3 until end of turn"
    /// SpellDefinition.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// (CR 608.2b — illegal target → no-op). When valid, registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(+3, -3) on the target's
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
                    // bot ranks opponent's biggest toughness-3-or-less threat
                    // via Removal intent (the -3 can kill it outright).
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
                        "Spatial Contortion — target creature gets +3/-3 until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // Register a +3/-3 EOT-scoped Layer 7c effect on the target creature.
        // Same pattern as DisfigureFactory / GraspOfDarknessFactory.
        // When ActiveEffects is null (shape tests without a live
        // ContinuousEffectsService), the effect registration is a no-op.
        if (target.ActiveEffects == null) return;
        target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, 3, -3));
    }
}
