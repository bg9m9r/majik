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
/// Named-card factory for Firebolt (Odyssey, {R}).
///
/// Sorcery. Scryfall oracle text (verbatim):
///   "Firebolt deals 2 damage to any target.
///    Flashback {4}{R} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// ## Implemented (v1)
/// - <b>Sorcery</b> shape, mana cost {R} (CardDef DSL). Same split-color
///   spike-cycle shape as <see cref="BumpInTheNightFactory"/> — a cheap red
///   damage spell whose only other cost on the card is the flashback line.
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "any target"
///   request — same target shape as <see cref="LightningBoltFactory"/>. On
///   resolution the chosen target takes <b>2 damage</b> through
///   <see cref="Fx.DealDamageAny"/>, so all four "any target" classes resolve
///   correctly (CR 115.3 — creature, player, planeswalker, or battle).
///   CR 306.7 — damage to a planeswalker becomes loyalty removal; CR 309.5 —
///   damage to a battle becomes defense removal; both are handled inside
///   <see cref="Fx.DealDamageAny"/>. This is the burn-spell counterpart to
///   Bump in the Night's life-loss body (CR 119 distinction): Firebolt deals
///   damage, so prevention shields / "if a source would deal damage"
///   replacements DO engage.
/// - <b>Flashback {4}{R}</b> (CR 702.34). The printed flashback cost is an
///   all-mana cost, so — mirroring <see cref="BumpInTheNightFactory"/> /
///   <see cref="FaithlessLootingFactory"/> — it is parsed out of
///   <see cref="OracleText"/> via <see cref="FlashbackOracleParser"/> and
///   surfaced as a <see cref="FlashbackAlternativeCost"/> through
///   <see cref="BuildFlashbackCost"/>. Callers thread the returned alt-cost
///   into <see cref="Majik.Core.Game.SpellCastFlow"/> when casting from the
///   graveyard; the post-resolution exile (CR 702.34b) is performed by the
///   cost's <c>OnResolved</c> hook (no extra wiring here). End-to-end path is
///   covered by <c>FireboltFactoryTests.Firebolt_FlashbackCast_*</c>.
/// </summary>
[CardName("Firebolt")]
public static class FireboltFactory
{
    public const string CardName = "Firebolt";
    public const string PrintedManaCost = "{R}";
    public const int Damage = 2;

    /// <summary>
    /// Oracle text reference. Drives <see cref="BuildFlashbackCost"/> via
    /// <see cref="FlashbackOracleParser"/> so the named-factory path and the
    /// data-driven oracle binder path agree on the {4}{R} flashback shape.
    /// </summary>
    public const string OracleText =
        "Firebolt deals 2 damage to any target.\n" +
        "Flashback {4}{R} (You may cast this card from your graveyard for its " +
        "flashback cost. Then exile it.)";

    /// <summary>CardDef DSL — card shape only (Sorcery, {R}). Damage body is
    /// supplied at cast time via <see cref="BuildSpellDefinition"/> (the
    /// runtime needs the caller's target resolver from the
    /// <see cref="GameContext"/>); flashback cost in
    /// <see cref="BuildFlashbackCost"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Firebolt is cast
    /// (printed cost or flashback). Single 1..1 "any target" request; on
    /// resolution deals <see cref="Damage"/> (2) damage to the chosen target
    /// through <see cref="Fx.DealDamageAny"/>.
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
                    Fx.Inline("Firebolt: 2 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }

    /// <summary>
    /// Build the flashback alternative cost ({4}{R}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here) keeps
    /// the named-factory path and the data-driven oracle binder path agreeing
    /// on shape — any change to the parser's interpretation of
    /// "Flashback {4}{R}" flows through to this factory automatically.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Firebolt's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
