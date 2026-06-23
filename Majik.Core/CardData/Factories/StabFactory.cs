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
/// Named-card factory for Stab (Tarkir: Dragonstorm, {B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-23):
///   "Target creature gets -2/-2 until end of turn."
///
/// Functionally identical to <see cref="DisfigureFactory"/> (same cost, same
/// effect); the only difference is the base shape is materialised from the
/// embedded JSON definition (<c>stab.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/> — same posture as
/// <see cref="BileBlightFactory"/>. The resolve behaviour (the -2/-2 EOT pump,
/// which the JSON <c>SpellDefinition</c> schema does not yet express) is layered
/// on here via <see cref="BuildDefinition"/>.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {B}, owner / controller — from JSON.
/// - <b>Target creature</b> — a single 1..1 "target creature"
///   <see cref="TargetRequest"/>; the live <c>CandidateGatherer</c> walks every
///   player's battlefield, yielding creatures.
/// - <b>-2/-2 until end of turn</b> — on resolution, after a CR 608.2b re-check
///   that the target is still a creature on the battlefield, registers a
///   <see cref="PumpUntilEndOfTurnEffect"/>(-2, -2) on the target creature's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at EOT; CR 613
///   Layer 7c). When <see cref="Creature.ActiveEffects"/> is null (shape-only
///   tests without a live ContinuousEffectsService) the registration is a
///   silent no-op.
/// </summary>
[CardName("Stab")]
public static class StabFactory
{
    public const string CardName = "Stab";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "stab";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {B}) from the
    /// embedded JSON definition. Resolve behaviour (-2/-2 until end of turn) is
    /// built on demand via <see cref="BuildDefinition"/>, mirroring
    /// <see cref="BileBlightFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "target creature gets -2/-2 until end of turn"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a <see cref="Creature"/> on the
    /// Battlefield (CR 608.2b — illegal target → no-op). When valid, registers a
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
                    // bot ranks the opponent's biggest small threat via Removal
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
                        "Stab — target creature gets -2/-2 until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // Register a -2/-2 EOT-scoped Layer 7c effect on the target creature.
        // Same pattern as DisfigureFactory. When ActiveEffects is null (shape
        // tests without a live ContinuousEffectsService) the registration is a
        // silent no-op.
        if (target.ActiveEffects == null) return;
        target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, -2, -2));
    }
}
