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
/// Named-card factory for Nature's Claim (Worldwake, {G}).
///
/// Instant. Oracle text (verified against Scryfall 2026-05-29):
///   "Destroy target artifact or enchantment. Its controller gains 4 life."
///
/// Direct analogue of <see cref="DisenchantFactory"/> (destroy target artifact
/// or enchantment) with a fixed 4-life rider granted to that permanent's
/// controller (CR 119.3). The "Its controller" referent is the controller of
/// the destroyed permanent, NOT the caster of Nature's Claim.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {G}, Green. The base card shape (name /
///   Instant type / {G} cost) is materialised from the embedded JSON
///   definition (<c>natures-claim.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/>.
/// - <b>Destroy target artifact or enchantment</b> — single 1..1
///   "target artifact or enchantment" <see cref="TargetRequest"/>. The
///   <c>CandidateGatherer</c> walks every player's battlefield, yielding
///   permanents that have type Artifact or Enchantment (CR 301–303).
/// - On resolution: re-checks the target is still a Permanent on the
///   Battlefield (CR 608.2b illegal-target gate) and has type Artifact or
///   Enchantment; captures that permanent's controller; destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7); then the captured
///   controller gains 4 life (CR 119.3).
///
/// The 4-life rider is unconditional in MTG — "Its controller gains 4 life"
/// happens even if the permanent is indestructible or otherwise survives the
/// destroy attempt (the destroy and the life gain are independent clauses of
/// a single resolving spell, CR 608.2). The life gain therefore fires whenever
/// the target is still a legal artifact/enchantment at resolution, regardless
/// of whether the Destroy reason actually removes it (Indestructible — CR
/// 702.12; regeneration — CR 701.15 — are honoured at the destroy site).
/// </summary>
[CardName("Nature's Claim")]
public static class NaturesClaimFactory
{
    public const string CardName = "Nature's Claim";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "natures-claim";

    /// <summary>Life gained by the destroyed permanent's controller (CR 119.3).</summary>
    public const int LifeGain = 4;

    /// <summary>
    /// Construct Nature's Claim as an Instant owned by <paramref name="owner"/>.
    /// Base shape (name / Instant / {G}) from the embedded JSON.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "destroy target artifact or enchantment; its controller gains
    /// 4 life" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates that the resolved target is still a
    /// <see cref="Permanent"/> on the Battlefield AND has type
    /// <see cref="CardType.Artifact"/> or <see cref="CardType.Enchantment"/>
    /// (CR 608.2b — illegal target at resolution → no-op); captures the
    /// permanent's controller; destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7); then the captured
    /// controller gains 4 life (CR 119.3).
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
                    Description: "target artifact or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are artifacts or enchantments (CR 301–303).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Enchantment))
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
                        $"{CardName}: destroy target artifact or enchantment; "
                        + $"its controller gains {LifeGain} life",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must be artifact or
                            // enchantment at resolution (CR 608.2b).
                            if (!target.HasType(CardType.Artifact)
                                && !target.HasType(CardType.Enchantment)) return;

                            // "Its controller" — the controller of the
                            // destroyed permanent, captured BEFORE the destroy
                            // (the move clears the controller reference).
                            var controller = target.Controller;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);

                            // CR 119.3 — "Its controller gains 4 life." Fires
                            // unconditionally as part of the same resolution,
                            // independent of whether the destroy removed the
                            // permanent (CR 608.2).
                            controller?.GainLife(LifeGain);
                        }),
                };
            });
    }
}
