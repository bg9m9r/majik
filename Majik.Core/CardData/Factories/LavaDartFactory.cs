using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Lava Dart (Time Spiral, {R}).
///
/// Instant. Oracle text:
///   "Lava Dart deals 1 damage to any target.
///    Flashback—Sacrifice a Mountain. (You may cast this card from your
///    graveyard for its flashback cost. Then exile it.)"
///
/// ## Implemented (v1)
/// - <b>Instant</b> shape, mana cost {R} (CardDef DSL).
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "any target"
///   request; on resolution deals 1 damage to the chosen target through
///   <see cref="Fx.DealDamageAny"/> so Player / Creature / Planeswalker
///   targets are routed correctly (CR 306.7 — damage to planeswalker
///   becomes loyalty removal). Mirrors Pyrite Spellbomb / Lightning Bolt
///   / Burst Lightning's resolve shape.
/// - <b>Flashback—Sacrifice a Mountain</b> (CR 702.34). The printed
///   flashback cost is non-mana ("Sacrifice a Mountain"), so v1 splits
///   the cost the same way <see cref="CabalTherapyFactory"/> does —
///   <see cref="FlashbackAlternativeCost"/> carries <see cref="ManaCost.Zero"/>
///   for the mana portion, and the sacrifice rider ships separately as
///   a <see cref="SacrificeBasicLandCost"/> wrapped in
///   <see cref="BuildFlashbackAdditionalCosts"/>. Callers compose both
///   when threading the flashback cast through
///   <see cref="SpellCastFlow"/>'s <c>additionalCosts</c> parameter.
///   Post-resolve exile (CR 702.34b) is handled by
///   <see cref="FlashbackAlternativeCost.OnResolved"/>.
///
/// ## End-to-end harness reference
/// <see cref="Majik.Core.Tests.Costs.LavaDartFlashbackTests"/> exercises
/// the full alt-cost path through <see cref="SpellCastFlow"/> using
/// directly-constructed costs (no factory). This factory is the
/// production-shape mirror — same cost shape, same effect body, same
/// post-resolve exile — surfaced via <see cref="BuildSpellDefinition"/> +
/// <see cref="BuildFlashbackCost"/> + <see cref="BuildFlashbackAdditionalCosts"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Multi-Mountain agent picker</b>: <see cref="BuildFlashbackAdditionalCosts"/>
///   requires the caller to pre-pick the specific Mountain to sacrifice —
///   same posture as <see cref="SacrificeBasicLandCost"/>'s constructor.
///   When a sacrifice-prompt agent surface ships, the convenience builder
///   can pick the first Mountain (deterministic) without changing callers.
/// </summary>
[CardName("Lava Dart")]
public static class LavaDartFactory
{
    public const string CardName = "Lava Dart";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 1;

    /// <summary>
    /// Oracle text reference. Lava Dart's printed flashback cost is
    /// "Sacrifice a Mountain" — non-mana, so the mana portion parses to
    /// <see cref="ManaCost.Zero"/>. Kept here for documentation; the
    /// flashback cost is built directly by <see cref="BuildFlashbackCost"/>
    /// (no parser round-trip needed) but
    /// <see cref="Majik.Core.CardData.FlashbackOracleParser"/> agrees on
    /// the same shape (see <c>LavaDartFlashbackTests.Parser_*</c>).
    /// </summary>
    public const string OracleText =
        "Lava Dart deals 1 damage to any target.\n" +
        "Flashback—Sacrifice a Mountain. " +
        "(You may cast this card from your graveyard for its flashback cost. " +
        "Then exile it.)";

    /// <summary>CardDef DSL — card shape only. Damage body + flashback
    /// cost shape are built via <see cref="BuildSpellDefinition"/> /
    /// <see cref="BuildFlashbackCost"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Lava Dart is cast
    /// (printed cost or flashback). Single 1..1 "any target" request; on
    /// resolution routes 1 damage through <see cref="Fx.DealDamageAny"/>.
    /// </summary>
    /// <param name="resolver">Target resolver supplied by the caller's
    /// <see cref="GameContext"/> (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest("any target", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Lava Dart: 1 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }

    /// <summary>
    /// Build the flashback alternative cost. Lava Dart's printed flashback
    /// cost is "Sacrifice a Mountain" — non-mana — so the returned cost
    /// carries <see cref="ManaCost.Zero"/>. The sacrifice rider ships
    /// separately via <see cref="BuildFlashbackAdditionalCosts"/>; callers
    /// compose both when wiring the flashback cast through
    /// <see cref="SpellCastFlow"/>. Post-resolve exile (CR 702.34b) is
    /// handled by <see cref="FlashbackAlternativeCost.OnResolved"/> (same
    /// pattern as Cabal Therapy / Faithless Looting).
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost() =>
        new FlashbackAlternativeCost(ManaCost.Zero);

    /// <summary>
    /// Build the additional-cost rider that accompanies the flashback
    /// alt-cost — "Sacrifice a Mountain" wrapped as a
    /// <see cref="SacrificeBasicLandCost"/> on the caller-picked Mountain
    /// (CR 601.2f / CR 702.34). Returned as a single-element list to match
    /// the shape <see cref="SpellCastFlow"/> threads through its
    /// <c>additionalCosts</c> parameter.
    /// </summary>
    /// <param name="mountain">The specific Mountain permanent to
    /// sacrifice. Caller responsibility — same posture as
    /// <see cref="SacrificeBasicLandCost"/>'s constructor (no auto-pick so
    /// multi-Mountain boards keep agent control over which dies).</param>
    public static IReadOnlyList<IAdditionalCost> BuildFlashbackAdditionalCosts(ICard mountain)
    {
        ArgumentNullException.ThrowIfNull(mountain);
        return new IAdditionalCost[]
        {
            new SacrificeBasicLandCost(mountain, CardSubtype.Mountain),
        };
    }
}
