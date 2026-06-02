using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Magmatic Sinkhole (Modern Horizons 3, {5}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Delve (Each card you exile from your graveyard while casting this
///    spell pays for {1}.)
///    Magmatic Sinkhole deals 5 damage to target creature or planeswalker."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {5}{R}.
/// - "Delve" marker keyword via <see cref="KeywordAbility"/> so downstream
///   code (UI, bot probes, action validator) can introspect the keyword.
///   The actual Delve mechanic (CR 702.66) lives in
///   <see cref="Majik.Core.Costs.DelveCost"/> + <see cref="SpellCastFlow"/>;
///   callers cast Magmatic Sinkhole via the cast-flow's <c>delveCost</c>
///   parameter when they want to substitute graveyard exiles for generic mana
///   — same wire-up as Murderous Cut / Treasure Cruise / Dig Through Time.
/// - On-resolve "deals 5 damage to target creature or planeswalker" effect,
///   exposed via <see cref="BuildSpellDefinition"/>. Single 1..1 "target
///   creature or planeswalker" request (same target shape as
///   <see cref="RipApartFactory"/>'s mode-0 damage clause). On resolution
///   deals <see cref="Damage"/> (5) damage via
///   <see cref="Fx.DealDamageAny(object, int)"/> (CR 119 / CR 306.7 — damage
///   to a planeswalker removes that much loyalty). CR 608.2b — if the resolved
///   object is neither a creature nor a planeswalker (illegal target due to a
///   zone/type change after targeting), the effect is a no-op.
///
/// ## Bot-side discovery
/// - <see cref="Majik.Core.Players.Agents.DelveAltCostProbe"/> surfaces
///   Magmatic Sinkhole to the heuristic bot via the Delve
///   <see cref="KeywordAbility"/> marker, as a
///   <see cref="Majik.Core.Costs.DelveAlternativeCost"/>.
/// </summary>
[CardName("Magmatic Sinkhole")]
public static class MagmaticSinkholeFactory
{
    public const string CardName = "Magmatic Sinkhole";
    public const string PrintedManaCost = "{5}{R}";

    /// <summary>Damage dealt to the chosen creature or planeswalker.</summary>
    public const int Damage = 5;

    /// <summary>CardDef DSL — card shape + Delve marker (CR 702.66).
    /// The damage body is built via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef
        .Instant(CardName, PrintedManaCost)
        .WithKeyword("Delve");

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the resolve-time <see cref="SpellDefinition"/>. Single 1..1
    /// "target creature or planeswalker" request; on resolution the chosen
    /// permanent is dealt 5 damage (CR 119 / CR 306.7) via
    /// <see cref="Fx.DealDamageAny(object, int)"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object). Pass
    /// <c>o =&gt; o</c> for tests that hand permanents directly.</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature or planeswalker",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal,
                    // Agent-prompt: every creature + planeswalker on the
                    // battlefield across all players (CR 302 / CR 306).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                                 || c.HasType(CardType.Planeswalker))
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect($"{CardName}: {Damage} damage to target creature or planeswalker", () =>
                    {
                        // CR 608.2b — only creatures and planeswalkers are legal
                        // targets; anything else (e.g. the target left the
                        // battlefield) is a no-op rather than redirecting damage.
                        if (raw is not (Creature or Planeswalker)) return;

                        // CR 119 / CR 306.7 — deal 5 damage; a planeswalker
                        // target loses that much loyalty via Fx.DealDamageAny.
                        Fx.DealDamageAny(raw, Damage);
                    }),
                };
            });
    }
}
