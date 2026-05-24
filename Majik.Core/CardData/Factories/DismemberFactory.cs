using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dismember (New Phyrexia, {1}{B/P}{B/P}).
///
/// Instant. Oracle text:
///   "({B/P} can be paid with either {B} or 2 life each.)
///    Target creature gets -5/-5 until end of turn."
///
/// ## Implemented (v1)
/// - Instant card with printed mana cost {1}{B}{B} (the mana-only pips of the
///   two phyrexian {B/P} symbols). CR 107.4f — each phyrexian pip may be paid
///   with {B} OR 2 life. Two phyrexian pips = 4 life total via the
///   all-life alternative (CR 118.8).
/// - Phyrexian alt cost (both pips paid as life): AlternativeManaCost = {1},
///   LifeCost = 4. Exposed via <see cref="PhyrexianAlternativeCost"/>.
/// - Structural Phyrexian mana marker via <see cref="KeywordAbility"/>("Phyrexian").
/// - <see cref="BuildDefinition"/> wires the resolve effect:
///   1..1 "target creature" TargetRequest. On resolve: register a
///   <see cref="PumpUntilEndOfTurnEffect"/>(-5, -5) on the target creature's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — "until end of turn").
///   CR 608.2b: target not on battlefield → no-op.
///
/// ## Deferred (v1 gaps)
/// - Per-pip selectivity (pay one pip as mana, one as life for {1}{B}{B/P}
///   intermediate cost) — <see cref="PhyrexianManaAlternativeCost"/> only
///   models "pay every phyrexian pip as life". The caller decides which
///   path to use.
/// </summary>
[CardName("Dismember")]
public static class DismemberFactory
{
    public const string CardName = "Dismember";

    /// <summary>
    /// Printed mana cost (the {B}{B} pips of the two phyrexian {B/P}
    /// symbols, plus the generic {1}). The 4-life alternative (both pips
    /// paid as life) reduces this to {1}; see <see cref="PhyrexianAlternativeCost"/>.
    /// </summary>
    public const string PrintedManaCost = "{1}{B}{B}";

    /// <summary>CardDef DSL — card shape only. CR 107.4f Phyrexian marker
    /// (the two {B/P} pips) is wired via <see cref="CardDefBuilder.WithKeyword"/>;
    /// the -5/-5 pump SpellDefinition lives in <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .WithKeyword("Phyrexian");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Returns a <see cref="PhyrexianManaAlternativeCost"/> for the two
    /// {B/P} pips: AlternativeManaCost = {1} (non-phyrexian portion after
    /// stripping both phyrexian pips), LifeCost = 4 (2 per pip).
    ///
    /// Callers that want the 4-life all-phyrexian cast supply this as
    /// <c>alternativeCost</c> to SpellCastFlow.CastAsync.
    /// </summary>
    public static PhyrexianManaAlternativeCost PhyrexianAlternativeCost()
        => PhyrexianManaAlternativeCost.ForPrintedCost(ManaCost.Parse("{1}{B/P}{B/P}"));

    /// <summary>
    /// Build the "target creature gets -5/-5 until end of turn" SpellDefinition.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// (CR 608.2b — illegal target → no-op). When valid, registers a
    /// <see cref="PumpUntilEndOfTurnEffect"/>(-5, -5) on the target's
    /// <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at EOT).
    /// When ActiveEffects is null (shape-only tests), the registration is
    /// a no-op.
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
                    // Agent-prompt MVP: enumerate every creature live; bot
                    // ranks opponent's biggest threat via Removal intent.
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
                        "Dismember — target creature gets -5/-5 until end of turn",
                        () => Resolve(raw)),
                };
            });

    private static void Resolve(object raw)
    {
        // CR 608.2b — target must still be a creature on the battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // Register a -5/-5 EOT-scoped Layer 7c effect on the target creature.
        // Same pattern as TheMeathookMassacreFactory / EarthshakerKhenraFactory.
        // When ActiveEffects is null (shape tests without a live
        // ContinuousEffectsService), the effect registration is a no-op.
        if (target.ActiveEffects == null) return;
        target.ActiveEffects.Register(new PumpUntilEndOfTurnEffect(target, -5, -5));
    }
}
