using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Become Immense (Khans of Tarkir, {5}{G}).
///
/// Instant. Oracle text:
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    Target creature gets +6/+6 until end of turn."
///
/// ## Implemented (v1)
/// - Instant shape, printed cost {5}{G}.
/// - "Delve" marker keyword via <see cref="KeywordAbility"/> so downstream
///   code (UI, bot probes, action validator) can introspect the keyword.
///   The actual Delve mechanic (CR 702.66) lives in
///   <see cref="Majik.Core.Costs.DelveCost"/> + <see cref="SpellCastFlow"/>;
///   callers cast Become Immense via the cast-flow's <c>delveCost</c>
///   parameter when they want to substitute graveyard exiles for generic
///   mana — same wire-up as Treasure Cruise / Murderous Cut.
/// - Resolve-time <see cref="SpellDefinition"/> via
///   <see cref="BuildSpellDefinition"/> declares a single 1..1
///   "target creature" request. On resolution the targeted creature gets
///   +6/+6 until end of turn, registered as a
///   <see cref="PumpUntilEndOfTurnEffect"/> on the target's
///   <see cref="Creature.ActiveEffects"/> (CR 514.2 — expires at cleanup).
/// - CR 608.2b — illegal target / missing continuous-effects service →
///   no-op (no throw).
///
/// ## Bot-side discovery
/// - <see cref="Majik.Core.Players.Agents.DelveAltCostProbe"/> surfaces
///   Become Immense to the heuristic bot's
///   <see cref="Majik.Core.Players.Agents.IAlternativeCostProbe"/> stream
///   via the Delve <see cref="KeywordAbility"/> marker, as a
///   <see cref="Majik.Core.Costs.DelveAlternativeCost"/>.
/// </summary>
[CardName("Become Immense")]
public static class BecomeImmenseFactory
{
    public const string CardName = "Become Immense";
    public const string PrintedManaCost = "{5}{G}";

    /// <summary>Layer 7c pump magnitude.</summary>
    public const int Pump = 6;

    /// <summary>CardDef DSL — card shape + Delve marker (CR 702.66).
    /// The pump body is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .WithKeyword("Delve");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time "+6/+6 until end of turn" SpellDefinition.
    /// Single 1..1 "target creature" request. On resolution the targeted
    /// creature gets +6/+6 EOT (CR 514.2). When the resolver returns a
    /// non-Creature or the target has no live
    /// <see cref="ContinuousEffectsService"/>, the effect is a no-op
    /// (CR 608.2b).
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("target creature", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: target creature gets +{Pump}/+{Pump} until end of turn", () =>
                    {
                        // CR 608.2b — fizzle on illegal target at resolve time.
                        if (raw is not Creature target) return;
                        if (target.Zone != ZoneType.Battlefield) return;
                        if (target.ActiveEffects == null) return;

                        // Layer 7c +P/+T grant with EOT expiry. Same pattern
                        // as MutagenicGrowthFactory.
                        target.ActiveEffects.Register(
                            new PumpUntilEndOfTurnEffect(target, Pump, Pump));
                    }),
                };
            });
    }
}
