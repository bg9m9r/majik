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
/// Named-card factory for Geistflame (Innistrad, {R}).
///
/// Instant. Scryfall oracle text (verbatim):
///   "Geistflame deals 1 damage to any target.
///    Flashback {3}{R} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// Geistflame composes two shapes the engine already supports:
/// - The <b>burn body</b> is identical to <see cref="LavaDartFactory"/> /
///   <see cref="ShockFactory"/> — a single 1..1 "any target" request that
///   deals 1 damage via <see cref="Fx.DealDamageAny"/> (CR 115.3 — "any
///   target" = creature, player, planeswalker, or battle; CR 120.3 /
///   CR 306.7 — damage to a planeswalker becomes loyalty removal).
/// - The <b>Flashback {3}{R}</b> rider (CR 702.34). The printed flashback
///   cost is an all-mana cost, so — mirroring <see cref="BumpInTheNightFactory"/>
///   / <see cref="FaithlessLootingFactory"/> — it is parsed out of
///   <see cref="OracleText"/> via <see cref="FlashbackOracleParser"/> and
///   surfaced as a <see cref="FlashbackAlternativeCost"/> through
///   <see cref="BuildFlashbackCost"/>. Callers thread the returned alt-cost
///   into <see cref="Majik.Core.Game.SpellCastFlow"/> when casting from the
///   graveyard; the post-resolution exile (CR 702.34b) is performed by the
///   cost's <c>OnResolved</c> hook (no extra wiring here).
///
/// Card shape comes from the embedded JSON (<c>geistflame.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> (same load posture as
/// <see cref="StaggershockFactory"/>). The resolve-time damage body lives
/// in <see cref="BuildSpellDefinition"/> because a
/// <see cref="SpellDefinition"/> needs a target resolver supplied by the
/// caller's <see cref="GameContext"/> (not expressible in the data-only
/// JSON schema).
/// </summary>
[CardName("Geistflame")]
public static class GeistflameFactory
{
    public const string CardName = "Geistflame";
    public const string Slug = "geistflame";
    public const string PrintedManaCost = "{R}";

    /// <summary>CR 119 / CR 120.3 — fixed 1 damage to any target.</summary>
    public const int Damage = 1;

    /// <summary>
    /// Oracle text reference. Drives <see cref="BuildFlashbackCost"/> via
    /// <see cref="FlashbackOracleParser"/> so the named-factory path and the
    /// data-driven oracle binder path agree on the {3}{R} flashback shape.
    /// </summary>
    public const string OracleText =
        "Geistflame deals 1 damage to any target.\n" +
        "Flashback {3}{R} (You may cast this card from your graveyard for its " +
        "flashback cost. Then exile it.)";

    /// <summary>
    /// Build the card shape from the embedded JSON definition (Instant, {R}).
    /// Damage body + flashback cost shape are built via
    /// <see cref="BuildSpellDefinition"/> / <see cref="BuildFlashbackCost"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Geistflame is cast
    /// (printed cost or flashback). Single 1..1 "any target" request, no X.
    /// On resolution routes <see cref="Damage"/> (1) damage through
    /// <see cref="Fx.DealDamageAny"/> (CR 120.3).
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
                    Fx.Inline("Geistflame: 1 damage to any target", () =>
                        Fx.DealDamageAny(target, Damage)),
                };
            });
    }

    /// <summary>
    /// Build the flashback alternative cost ({3}{R}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here) keeps
    /// the named-factory path and the data-driven oracle binder path agreeing
    /// on shape — any change to the parser's interpretation of
    /// "Flashback {3}{R}" flows through to this factory automatically
    /// (CR 702.34). Post-resolve exile (CR 702.34b) is handled by
    /// <see cref="FlashbackAlternativeCost.OnResolved"/>.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Geistflame's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
