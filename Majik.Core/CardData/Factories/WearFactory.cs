using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the WEAR half of the split card Wear // Tear
/// (Dragon's Maze, {1}{R} // {W}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-01):
///   "Destroy target artifact.
///    Fuse (You may cast one or both halves of this card from your hand.)"
///
/// Sister half — <see cref="TearFactory"/> ({W}; "Destroy target enchantment.
/// Fuse ...").
///
/// ## Split-card modelling (CR 712 / CR 709)
///
/// A split card is a single physical card with two halves; the caster picks
/// one half on cast and casts only that half. v1 models each printed half as
/// its own <c>[CardName]</c>-dispatched factory — the same minimal posture the
/// engine uses for Fire // Ice (<see cref="FireFactory"/> /
/// <see cref="IceFactory"/>):
/// <list type="bullet">
///   <item>Casting Wear → <see cref="NamedCardFactory"/> resolves
///     <c>"Wear"</c> → this factory → an <see cref="Instant"/> with the
///     destroy-artifact effect.</item>
///   <item>Casting Tear → <see cref="NamedCardFactory"/> resolves
///     <c>"Tear"</c> → <see cref="TearFactory"/> → an <see cref="Instant"/>
///     with the destroy-enchantment effect.</item>
/// </list>
/// The combined seed row <c>"Wear // Tear"</c> flips <c>IsImplemented</c> via
/// the front-face check in <see cref="EmbeddedCardRepository"/> because the
/// front half <c>"Wear"</c> is in the <see cref="ImplementedCardNames"/>
/// registry. Each half also carries an <see cref="MdfcState"/> face tracker
/// (front = "Wear", back = "Tear") so callers can observe the other half's
/// printed name from either object — same informational role MdfcState plays
/// for the Fire // Ice split.
///
/// ## Implemented (v1)
/// - Instant identity at {1}{R} (red, mana value 2), built from the embedded
///   JSON def (<c>wear.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="MdfcState"/> attached on the front half (Wear).
/// - <b>Destroy target artifact</b> — single 1..1 "target artifact"
///   <see cref="TargetRequest"/>; the <c>CandidateGatherer</c> walks every
///   player's battlefield, yielding permanents that have type Artifact
///   (CR 301). Identical destroy shape to <see cref="AncientGrudgeFactory"/>.
///   On resolution it re-checks the target is still a Permanent on the
///   Battlefield with type Artifact (CR 608.2b illegal-target gate), then
///   destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///   (CR 702.12) / regeneration (CR 701.15) shields are honoured.
///
/// ## Deferred (v1 gap — shared with Fire // Ice)
/// - <b>Fuse</b> (CR 702.102) — casting BOTH halves from hand as one split
///   spell. The engine has no split-cast / fuse cast surface yet, so the Fuse
///   keyword is informational only; each half is castable independently via
///   its own <c>[CardName]</c> factory.
/// </summary>
[CardName("Wear")]
public static class WearFactory
{
    public const string CardName = "Wear";
    public const string SisterName = "Tear";
    public const string Slug = "wear";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>
    /// Build the Wear half as an Instant from the embedded JSON def, with the
    /// <see cref="MdfcState"/> face tracker attached (front = Wear).
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 712 — attach the split-card face tracker so the sister half's
        // printed name (Tear) is observable from the Wear object. Starts on
        // the front half. Informational only, matching the Fire // Ice posture.
        card.MdfcState = new MdfcState(CardName, SisterName);
        return card;
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
    /// regeneration shields are honoured. Mirrors
    /// <see cref="AncientGrudgeFactory"/>.
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
}
