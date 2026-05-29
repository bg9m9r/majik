using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ancient Grudge (Time Spiral, {1}{R}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target artifact.
///    Flashback {G} (You may cast this card from your graveyard for its
///    flashback cost. Then exile it.)"
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>ancient-grudge.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> (same data-only shape as
/// <see cref="PlayWithFireFactory"/>). The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
///
/// - <b>Destroy target artifact</b> — <see cref="BuildDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target artifact"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding permanents that have type Artifact
///   (CR 301). Identical destroy shape to <see cref="ShatterFactory"/>.
///   On resolution it re-checks the target is still a Permanent on the
///   Battlefield with type Artifact (CR 608.2b illegal-target gate), then
///   destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///   (CR 702.12) / regeneration (CR 701.15) shields are honoured.
/// - <b>Printed Flashback {G}</b> (CR 702.34) alt-cost: produced via
///   <see cref="GetFlashbackAlternativeCost"/> so callers (bots / integration
///   tests) can cast Ancient Grudge from the graveyard via
///   <see cref="FlashbackAlternativeCost"/>. Post-resolve exile (CR 702.34b)
///   is handled by <see cref="FlashbackAlternativeCost.OnResolved"/> — same
///   alt-cost wiring as every other printed-flashback card
///   (<see cref="PastInFlamesFactory"/> / <see cref="FaithlessLootingFactory"/>).
/// </summary>
[CardName("Ancient Grudge")]
public static class AncientGrudgeFactory
{
    public const string CardName = "Ancient Grudge";
    public const string Slug = "ancient-grudge";
    public const string PrintedManaCost = "{1}{R}";
    public const string FlashbackManaCost = "{G}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "destroy target artifact" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the resolved target is still a
    /// <see cref="Permanent"/> on the Battlefield AND has type
    /// <see cref="CardType.Artifact"/> (CR 608.2b — illegal target at
    /// resolution → no-op); then destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible /
    /// regeneration shields are honoured. Mirrors <see cref="ShatterFactory"/>.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target artifact",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are artifacts (CR 301).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact))
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
                        $"{CardName}: destroy target artifact",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must be an artifact at
                            // resolution (CR 608.2b).
                            if (!target.HasType(CardType.Artifact)) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }

    /// <summary>
    /// Build the <see cref="FlashbackAlternativeCost"/> for Ancient Grudge —
    /// the printed Flashback {G} (CR 702.34). Callers cast Ancient Grudge from
    /// the graveyard by passing this alt-cost to the spell-cast flow; the
    /// post-resolve exile (CR 702.34b) is handled by
    /// <see cref="FlashbackAlternativeCost.OnResolved"/>.
    /// </summary>
    public static FlashbackAlternativeCost GetFlashbackAlternativeCost() =>
        new(ManaCost.Parse(FlashbackManaCost));
}
