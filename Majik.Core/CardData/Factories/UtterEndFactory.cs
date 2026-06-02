using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Utter End (Commander 2014, {2}{W}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Exile target nonland permanent."
///
/// ## Why it gets its own factory
/// Utter End is the W/B unconditional catch-all answer: it exiles ANY nonland
/// permanent regardless of indestructible/mana value. It mirrors the
/// exile-target-permanent resolve of <see cref="AnguishedUnmakingFactory"/> /
/// <see cref="DesparkFactory"/> but:
///   1. <b>No life loss</b> — unlike Anguished Unmaking, the printed text is a
///      single sentence with no "you lose N life" clause, so resolve is just
///      the exile.
///   2. <b>Nonland-permanent filter</b> — unlike Despark (mana value gate),
///      Utter End filters to "nonland permanent" (CR 305 — Land is a card
///      type; the filter rejects any permanent that includes the Land type,
///      e.g. Dryad Arbor). One more generic mana and one more sentence than
///      Despark, but the SAME exile primitive and target machinery.
/// All primitives already ship — no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{W}{B}. Card shape comes from the embedded
///   JSON (<c>utter-end.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Exile target nonland permanent</b> — <see cref="BuildDefinition"/>
///   returns a <see cref="SpellDefinition"/> with a single 1..1 target request
///   over the shared <c>nonland_permanent</c> filter. The live
///   <c>CandidateGatherer</c> walks every player's battlefield, yielding
///   permanents whose card-type set does NOT include
///   <see cref="CardType.Land"/> (CR 305).
/// - On resolution: re-checks the target is still a nonland permanent on the
///   Battlefield (CR 608.2b illegal-target gate); when valid, the target is
///   exiled (CR 701.21) via the shared exile primitive. Indestructible
///   (CR 702.12) does NOT prevent exile — the card moves regardless.
///
/// ## Rules citations
/// - CR 305 — Land is a card type (nonland filter).
/// - CR 608.2b — resolution-time legality re-check.
/// - CR 701.21 — Exile.
/// - CR 702.12 — Indestructible does not stop exile.
/// </summary>
[CardName("Utter End")]
public static class UtterEndFactory
{
    public const string CardName = "Utter End";
    public const string Slug = "utter-end";
    public const string PrintedManaCost = "{2}{W}{B}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "exile target nonland permanent" <see cref="SpellDefinition"/>.
    /// On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: target must still be a nonland permanent on
    ///     the Battlefield.</item>
    ///   <item>CR 701.21 — exile the target via the shared exile primitive
    ///     (same surface as <see cref="DesparkFactory"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="caster">The controller of Utter End.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <remarks>
    /// Declarative conversion (the exile-verb slice): delegates to the shared
    /// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> with a single
    /// <see cref="ExileTargetEffectDef"/> over the <c>nonland_permanent</c>
    /// filter. That filter's predicate IS the CR 608.2b legality (battlefield +
    /// not a Land type per CR 305), re-checked at resolution by the verb, routed
    /// through the same shared exile primitive
    /// (<see cref="Majik.Core.Primitives.Fx.MoveToExile(ICard)"/>).
    /// </remarks>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new ExileTargetEffectDef { TargetFilter = "nonland_permanent" },
            });
    }
}
