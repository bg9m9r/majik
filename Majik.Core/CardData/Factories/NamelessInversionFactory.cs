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
/// Named-card factory for Nameless Inversion (Lorwyn / various reprints,
/// {1}{B}).
///
/// Kindred Instant — Shapeshifter. Oracle text (verified against the prompt;
/// matches Scryfall):
///   "Changeling (This card is every creature type.)
///    Target creature gets +3/-3 and loses all creature types until end of
///    turn."
///
/// ## Implemented (v1)
///
/// - <b>Instant shape</b> at printed cost {1}{B}. The base shape
///   (name / Instant + Tribal types / Shapeshifter subtype / {1}{B} cost) is
///   materialised from the embedded JSON definition
///   (<c>nameless-inversion.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same JSON-first posture as
///   <see cref="TarfireFactory"/> (the other Kindred/Tribal instant in the
///   pool). The JSON lists <c>Instant</c> first so the concrete
///   <see cref="Instant"/> class is built, with <see cref="Majik.Core.Cards.Types.CardType.Tribal"/>
///   added as the secondary type and the printed Shapeshifter subtype.
/// - <b>Changeling (CR 702.73 / 312)</b> — modelled as a
///   <see cref="KeywordAbility"/> marker plus the printed Shapeshifter base
///   subtype carried on the JSON shape. This is the v1 Changeling posture used
///   by <see cref="MutableExplorerFactory"/>'s keyword-marker mechanism; the
///   full "is every creature type" subtype stamp is only relevant for
///   tribal-"matters" effects reading a <i>spell's</i> creature types, of
///   which the engine has no consumers yet. CR 702.73a (the ability works in
///   every zone) is observably satisfied by the static marker.
/// - <b>+3/-3 and loses all creature types until end of turn</b> —
///   <see cref="BuildDefinition"/> wires the resolve effect: a single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolve (target still a
///   creature on the battlefield, CR 608.2b), two end-of-turn-scoped
///   continuous effects are registered on the target's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — both expire at cleanup):
///     1. <see cref="PumpUntilEndOfTurnEffect"/>(+3, -3) — Layer 7c (same
///        pattern as <see cref="DisfigureFactory"/> / <see cref="DismemberFactory"/>).
///     2. <see cref="LoseAllCreatureTypesUntilEndOfTurnEffect"/> — Layer 4,
///        strips every creature subtype.
///   When <see cref="Creature.ActiveEffects"/> is null (shape-only tests
///   without a live ContinuousEffectsService) the registration is a no-op,
///   mirroring the Disfigure guard.
/// </summary>
[CardName("Nameless Inversion")]
public static class NamelessInversionFactory
{
    public const string CardName = "Nameless Inversion";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "nameless-inversion";

    /// <summary>Power delta applied to the target until end of turn.</summary>
    public const int PowerDelta = 3;

    /// <summary>Toughness delta applied to the target until end of turn.</summary>
    public const int ToughnessDelta = -3;

    /// <summary>
    /// Materialise the Kindred/Tribal Instant card shape from the embedded
    /// JSON definition, then attach the Changeling keyword marker
    /// (CR 702.73 / 312). Resolve behaviour (+3/-3 + lose all creature types
    /// until end of turn) is built on demand via <see cref="BuildDefinition"/>,
    /// mirroring <see cref="TarfireFactory"/>.
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

        // CR 702.73 / 312 — Changeling keyword marker. Observational: the
        // printed Shapeshifter subtype rides on the JSON shape; this marker
        // lets Changeling-aware enumerations detect the keyword without
        // scanning subtypes (same posture as MutableExplorerFactory).
        card.AddAbility(new KeywordAbility("Changeling", card, owner));

        return card;
    }

    /// <summary>
    /// Build the "target creature gets +3/-3 and loses all creature types
    /// until end of turn" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a <see cref="Creature"/> on
    /// the Battlefield (CR 608.2b — illegal target → no-op). When valid,
    /// registers a <see cref="PumpUntilEndOfTurnEffect"/>(+3, -3) and a
    /// <see cref="LoseAllCreatureTypesUntilEndOfTurnEffect"/> on the target's
    /// <see cref="Creature.ActiveEffects"/> (CR 514.2 — both expire at EOT).
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
                    // Removal intent ranks the opponent's best target. +3/-3
                    // is a kill on anything with toughness <= 3 (and a combat
                    // trick the rest of the time).
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
                        "Nameless Inversion — target creature gets +3/-3 and "
                        + "loses all creature types until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // Register the EOT-scoped Layer 7c (+3/-3) and Layer 4
        // (lose-all-creature-types) effects on the target. When ActiveEffects
        // is null (shape tests without a live ContinuousEffectsService) the
        // registration is a no-op — same guard as DisfigureFactory.
        if (target.ActiveEffects == null) return;
        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PowerDelta, ToughnessDelta));
        target.ActiveEffects.Register(
            new LoseAllCreatureTypesUntilEndOfTurnEffect(target));
    }
}
