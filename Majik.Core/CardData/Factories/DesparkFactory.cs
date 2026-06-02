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
/// Named-card factory for Despark (War of the Spark, {W}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Exile target permanent with mana value 4 or greater."
///
/// ## Why it gets its own factory
/// Despark is the W/B efficient catch-all answer to expensive permanents.
/// It mirrors the exile-target-permanent resolve of
/// <see cref="AnguishedUnmakingFactory"/> but with two differences:
///   1. <b>No life loss</b> — Despark's printed text is a single sentence
///      with no "you lose N life" clause, so the resolve is just the exile.
///   2. <b>Target filter is mana value, not permanent-type.</b> Where
///      Anguished Unmaking filters to "nonland permanent", Despark targets
///      ANY permanent (lands included) whose mana value is 4 or greater
///      (CR 202.3 — mana value). A basic land has mana value 0, so the gate
///      rejects it; a high-mv land card would be a legal target. The mv read
///      uses <see cref="Card.ManaCostValue"/>'s
///      <see cref="ValueObjects.ManaCost.TotalValue"/>, the same surface
///      <see cref="SkyclaveApparitionFactory"/> uses for its mv gate.
/// All primitives already ship — no new engine mechanic is required.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}{B}. Card shape comes from the embedded JSON
///   (<c>despark.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <b>Exile target permanent with mana value 4 or greater</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 target request. The live <c>CandidateGatherer</c>
///   walks every player's battlefield, yielding permanents whose mana value
///   is &gt;= 4 (CR 202.3).
/// - On resolution: re-checks the target is still a permanent on the
///   Battlefield with mana value &gt;= 4 (CR 608.2b illegal-target gate);
///   when valid, the target is exiled (CR 701.21) by routing through the
///   owning player's zones (mirrors <see cref="AnguishedUnmakingFactory"/>).
///   Indestructible (CR 702.12) does NOT prevent exile — the card moves
///   regardless.
///
/// ## Rules citations
/// - CR 202.3 — mana value.
/// - CR 608.2b — resolution-time legality re-check.
/// - CR 701.21 — Exile.
/// </summary>
[CardName("Despark")]
public static class DesparkFactory
{
    public const string CardName = "Despark";
    public const string Slug = "despark";
    public const string PrintedManaCost = "{W}{B}";

    /// <summary>CR 202.3 — minimum mana value of a legal target.</summary>
    public const int MinTargetManaValue = 4;

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "exile target permanent with mana value 4 or greater"
    /// <see cref="SpellDefinition"/>. On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: target must still be a permanent on the
    ///     Battlefield with mana value &gt;= 4.</item>
    ///   <item>CR 701.21 — exile the target via owner-routed zone moves
    ///     (same surface as <see cref="AnguishedUnmakingFactory"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="caster">The controller of Despark.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target permanent with mana value 4 or greater",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every permanent with mana
                    // value >= 4 on any battlefield (CR 202.3). Removal intent
                    // in the bot's ranker pushes opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c is Card card
                            && card.ManaCostValue.TotalValue >= MinTargetManaValue)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: exile target permanent with mana value 4 or greater (CR 701.21)",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // Exile only fires when the target is still a
                            // permanent on the battlefield with mana value >= 4.
                            if (resolved is Permanent target
                                && target.Zone == ZoneType.Battlefield
                                && target.ManaCostValue.TotalValue >= MinTargetManaValue)
                            {
                                // CR 701.21 — Exile. Routed through the owning
                                // player's zones so owner-of-zone bookkeeping
                                // stays consistent across multi-player games
                                // (mirrors AnguishedUnmaking / SkyclaveApparition).
                                // Indestructible (CR 702.12) does not prevent
                                // exile.
                                var fromOwner = target.Owner;
                                if (fromOwner != null)
                                {
                                    fromOwner.Zones.Battlefield.RemoveCard(target);
                                    fromOwner.Zones.Exile.AddCard(target);
                                }
                                target.SetZone(ZoneType.Exile);
                            }
                        }),
                };
            });
    }
}
