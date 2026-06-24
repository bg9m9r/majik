using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Moment of Craving (Dominaria / various reprints,
/// {1}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Target creature gets -2/-2 until end of turn. You gain 2 life."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, owner / controller — via the
///   <see cref="CardDef.Instant"/> DSL (same shape source as
///   <see cref="DisfigureFactory"/> / <see cref="GraspOfDarknessFactory"/>;
///   these simple instants carry no separate JSON definition).
/// - <b>-2/-2 until end of turn</b> + <b>You gain 2 life</b> — built on-demand
///   via <see cref="BuildSpellDefinition(Player, Func{object, object})"/>.
///   Single 1..1 "target creature" <see cref="TargetRequest"/>. On resolution
///   (one resolution, both clauses): register a
///   <see cref="PumpUntilEndOfTurnEffect"/>(-2, -2) on the target creature's
///   <see cref="Creature.ActiveEffects"/> (CR 613 Layer 7c / CR 514.2 —
///   expires at EOT), then the spell's controller gains 2 life (CR 119.3).
///   The lifegain combines a controller-side effect with the targeted pump,
///   so this needs the controller threaded in — same factory shape as
///   <see cref="LightningHelixFactory.BuildSpellDefinition"/> ("deal damage +
///   gain life"). The composite "debuff + you gain N life" text has no
///   data-driven spell template, so the body lives here.
///   CR 608.2b: target not on battlefield → the pump is a no-op; the lifegain
///   clause is part of the same resolution and still applies (mirrors
///   LightningHelix's unconditional "and you gain N life"). When ActiveEffects
///   is null (shape-only tests without a live ContinuousEffectsService) the
///   pump registration is a silent no-op (same guard as
///   <see cref="DisfigureFactory"/>).
/// </summary>
[CardName("Moment of Craving")]
public static class MomentOfCravingFactory
{
    public const string CardName = "Moment of Craving";
    public const string PrintedManaCost = "{1}{B}";

    public const int PowerReduction = -2;
    public const int ToughnessReduction = -2;
    public const int LifeGainAmount = 2;

    /// <summary>CardDef DSL — card shape only. The -2/-2 pump + lifegain body
    /// is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Moment of Craving is
    /// cast. Single 1..1 "target creature" request; on resolution the target
    /// gets -2/-2 until end of turn and the controller gains 2 life.
    /// </summary>
    /// <param name="controller">Spell controller — gains 2 life on
    /// resolution (CR 119.3).</param>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Player controller,
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(controller);
        ArgumentNullException.ThrowIfNull(resolver);

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
                    // Agent-prompt MVP: enumerate every creature live; the
                    // bot ranks the opponent's biggest small threat via the
                    // Removal intent. Mirrors DisfigureFactory.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline($"{CardName}: target creature -2/-2 EOT + gain 2 life", () =>
                    {
                        ApplyDebuff(raw);

                        // CR 119.3 — controller gains 2 life unconditionally as
                        // part of the same resolution.
                        Fx.GainLife(controller, LifeGainAmount);
                    }),
                };
            });
    }

    private static void ApplyDebuff(object raw)
    {
        // CR 608.2b — the pump can only apply to a creature still on the
        // battlefield.
        if (raw is not Creature target) return;
        if (target.Zone != ZoneType.Battlefield) return;

        // Register a -2/-2 EOT-scoped Layer 7c effect on the target creature
        // (CR 514.2 — cleanup discards it). Same pattern as DisfigureFactory.
        // When ActiveEffects is null (shape tests without a live
        // ContinuousEffectsService), the registration is a silent no-op.
        if (target.ActiveEffects == null) return;
        target.ActiveEffects.Register(
            new PumpUntilEndOfTurnEffect(target, PowerReduction, ToughnessReduction));
    }
}
