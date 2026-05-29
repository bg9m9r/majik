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
/// Named-card factory for Bump in the Night (Innistrad, {B}).
///
/// Sorcery. Scryfall oracle text (verbatim):
///   "Target opponent loses 3 life.
///    Flashback {5}{R} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// ## Implemented (v1)
/// - <b>Sorcery</b> shape, mana cost {B} (CardDef DSL). Note the printed
///   cost is black; the flashback cost is the only red mana on the card
///   (this is the "split-color flashback" hallmark of the Innistrad spike
///   cycle).
/// - Resolve-time <see cref="SpellDefinition"/> (via
///   <see cref="BuildSpellDefinition"/>) declares one 1..1 "target opponent"
///   request — same target shape as <see cref="DuressFactory"/> /
///   <see cref="CoercionFactory"/>. On resolution the chosen opponent
///   <b>loses 3 life</b> through <see cref="Fx.LoseLife"/>.
///   <para>
///   CR 119 — "loses life" is NOT damage. Routing through
///   <see cref="Fx.LoseLife"/> (rather than <see cref="Fx.DealDamage"/>,
///   the Lava Spike route) means damage-prevention shields, lifelink, and
///   "if a source would deal damage" replacement effects never engage —
///   exactly the rules distinction between Bump in the Night and a burn
///   spell like Lava Spike.
///   </para>
/// - <b>Flashback {5}{R}</b> (CR 702.34). The printed flashback cost is an
///   all-mana cost, so — mirroring <see cref="FaithlessLootingFactory"/> —
///   it is parsed out of <see cref="OracleText"/> via
///   <see cref="FlashbackOracleParser"/> and surfaced as a
///   <see cref="FlashbackAlternativeCost"/> through
///   <see cref="BuildFlashbackCost"/>. Callers thread the returned alt-cost
///   into <see cref="Majik.Core.Game.SpellCastFlow"/> when casting from the
///   graveyard; the post-resolution exile (CR 702.34b) is performed by the
///   cost's <c>OnResolved</c> hook (no extra wiring here). End-to-end path
///   is covered by <c>BumpInTheNightFactoryTests.BumpInTheNight_FlashbackCast_*</c>.
///
/// ## Deferred (v1 gaps)
/// - "Target opponent" candidate-pool gating uses the description string as
///   the TargetRequest label (same posture as Duress / Coercion / Lava
///   Spike's "target player or planeswalker"); the opponent constraint is
///   enforced at the agent/caller level until the targeting subsystem
///   carries a typed opponent filter.
/// </summary>
[CardName("Bump in the Night")]
public static class BumpInTheNightFactory
{
    public const string CardName = "Bump in the Night";
    public const string PrintedManaCost = "{B}";
    public const int LifeLoss = 3;

    /// <summary>
    /// Oracle text reference. Drives <see cref="BuildFlashbackCost"/> via
    /// <see cref="FlashbackOracleParser"/> so the named-factory path and the
    /// data-driven oracle binder path agree on the {5}{R} flashback shape.
    /// </summary>
    public const string OracleText =
        "Target opponent loses 3 life.\n" +
        "Flashback {5}{R} (You may cast this card from your graveyard for its " +
        "flashback cost. Then exile it.)";

    /// <summary>CardDef DSL — card shape only (Sorcery, {B}). Life-loss body
    /// is supplied at cast time via <see cref="BuildSpellDefinition"/> (the
    /// runtime needs the caller's target resolver from the
    /// <see cref="GameContext"/>); flashback cost in
    /// <see cref="BuildFlashbackCost"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Bump in the Night is
    /// cast (printed cost or flashback). Single 1..1 "target opponent"
    /// request; on resolution the chosen opponent loses
    /// <see cref="LifeLoss"/> (3) life via <see cref="Fx.LoseLife"/>
    /// (CR 119 — life loss, not damage).
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
                new TargetRequest("target opponent", 1, 1, Array.Empty<object>()),
            },
            EffectFactory: chosen =>
            {
                var target = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    Fx.Inline("Bump in the Night: target opponent loses 3 life", () =>
                    {
                        // CR 119 — life loss, not damage. Only a Player can
                        // lose life; a clean no-op for any other target shape
                        // (defensive — "target opponent" is always a Player).
                        if (target is Player opponent)
                        {
                            Fx.LoseLife(opponent, LifeLoss);
                        }
                    }),
                };
            });
    }

    /// <summary>
    /// Build the flashback alternative cost ({5}{R}) by running
    /// <see cref="OracleText"/> through <see cref="FlashbackOracleParser"/>.
    /// Going through the parser (rather than hard-coding the cost here) keeps
    /// the named-factory path and the data-driven oracle binder path agreeing
    /// on shape — any change to the parser's interpretation of
    /// "Flashback {5}{R}" flows through to this factory automatically.
    /// </summary>
    public static FlashbackAlternativeCost BuildFlashbackCost()
    {
        var descriptor = FlashbackOracleParser.TryParse(OracleText)
            ?? throw new InvalidOperationException(
                "FlashbackOracleParser failed to parse Bump in the Night's oracle text.");
        return new FlashbackAlternativeCost(descriptor.ManaCost);
    }
}
