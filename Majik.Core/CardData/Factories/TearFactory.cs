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
/// Named-card factory for the TEAR half of the split card Wear // Tear
/// (Dragon's Maze, {1}{R} // {W}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-01):
///   "Destroy target enchantment.
///    Fuse (You may cast one or both halves of this card from your hand.)"
///
/// Sister half — <see cref="WearFactory"/> ({1}{R}; "Destroy target artifact.
/// Fuse ...").
///
/// ## Split-card modelling (CR 712 / CR 709)
///
/// See <see cref="WearFactory"/> for the split-card posture. Each half is its
/// own <c>[CardName]</c>-dispatched factory; the combined seed row
/// <c>"Wear // Tear"</c> flips <c>IsImplemented</c> off the FRONT half
/// (<c>"Wear"</c>) via <see cref="EmbeddedCardRepository"/>. The Tear half
/// carries an <see cref="MdfcState"/> pre-flipped to the back half so the
/// front half's name (Wear) is still observable from the Tear object — the
/// same informational role MdfcState plays for the Ice back half
/// (<see cref="IceFactory"/>).
///
/// ## Implemented (v1)
/// - Instant identity at {W} (white, mana value 1), built from the embedded
///   JSON def (<c>tear.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back half (Tear).
/// - <b>Destroy target enchantment</b> — single 1..1 "target enchantment"
///   <see cref="TargetRequest"/>; the <c>CandidateGatherer</c> walks every
///   player's battlefield, yielding permanents that have type Enchantment
///   (CR 303). On resolution it re-checks the target is still a Permanent on
///   the Battlefield with type Enchantment (CR 608.2b illegal-target gate),
///   then destroys via
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
[CardName("Tear")]
public static class TearFactory
{
    public const string CardName = "Tear";
    public const string SisterName = "Wear";
    public const string Slug = "tear";
    public const string PrintedManaCost = "{W}";

    /// <summary>
    /// Build the Tear half as an Instant from the embedded JSON def, with the
    /// <see cref="MdfcState"/> face tracker attached, pre-flipped to the back
    /// half so the front half's name (Wear) stays observable.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Instant)CardDefinitionFactory.Build(def, owner);

        // CR 712 — split-card face tracker. Front = Wear, back = Tear; this
        // half is constructed pre-flipped to the back (Tear). Informational
        // only, matching the IceFactory posture.
        var mdfc = new MdfcState(SisterName, CardName);
        mdfc.Transform(); // flip to the back half (Tear)
        card.MdfcState = mdfc;
        return card;
    }

    /// <summary>
    /// Build the "destroy target enchantment" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the resolved target is still a
    /// <see cref="Permanent"/> on the Battlefield AND has type
    /// <see cref="CardType.Enchantment"/> (CR 608.2b — illegal target at
    /// resolution → no-op); then destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible /
    /// regeneration shields are honoured. Mirrors
    /// <see cref="NaturesClaimFactory"/> (enchantment branch, no life rider).
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
                    Description: "target enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are enchantments (CR 303).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Enchantment))
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
                        $"{CardName}: destroy target enchantment",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must be an enchantment
                            // at resolution (CR 608.2b).
                            if (!target.HasType(CardType.Enchantment)) return;

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
