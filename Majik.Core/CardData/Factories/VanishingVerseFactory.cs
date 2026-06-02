using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vanishing Verse (Strixhaven: School of Mages,
/// {W}{B}).
///
/// Instant. Oracle text:
///   "Exile target monocolored permanent."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {W}{B}, owner / controller.
/// - <b>Exile target monocolored permanent</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target monocolored permanent"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding permanents whose colour set has
///   exactly one colour (CR 105 — monocolored means exactly one colour;
///   colourless = 0 colours and multicolour = ≥2 colours are NOT legal),
///   via <see cref="CardColors.GetColors"/> — same filter posture as
///   <see cref="UltimatePriceFactory"/>.
/// - On resolution: re-checks the target is still a monocolored permanent
///   on the Battlefield (CR 608.2b illegal-target gate); when valid, the
///   target is exiled (CR 701.21) by routing through the owning player's
///   zones (mirrors <see cref="AnguishedUnmakingFactory"/> / Path to Exile).
///   Indestructible (CR 702.12) does NOT prevent exile — the card moves
///   regardless.
/// </summary>
[CardName("Vanishing Verse")]
public static class VanishingVerseFactory
{
    public const string CardName = "Vanishing Verse";
    public const string PrintedManaCost = "{W}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (exile
    /// target monocolored permanent) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "exile target monocolored permanent"
    /// <see cref="SpellDefinition"/>. On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: target must still be a Permanent on the
    ///     Battlefield and must be monocolored (CR 105 — exactly one
    ///     colour).</item>
    ///   <item>CR 701.21 — exile the target via owner-routed zone moves
    ///     (same surface as <see cref="AnguishedUnmakingFactory"/>).</item>
    /// </list>
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target
    /// token to the live engine object. Pass <c>o =&gt; o</c> for tests
    /// that hand permanents directly.</param>
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
                    Description: "target monocolored permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every monocolored
                    // permanent on any battlefield. Monocolored = exactly
                    // 1 colour (CR 105). Colorless (0 colours) and
                    // multicolor (≥2 colours) are NOT legal targets.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Permanent>()
                        .Where(c => CardColors.GetColors(c).Count == 1)
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
                        $"{CardName}: exile target monocolored permanent",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 105 — monocolored filter: exactly one colour.
                            // Colourless (Count == 0) and multicolour
                            // (Count >= 2) are not monocolored → no-op.
                            if (CardColors.GetColors(target).Count != 1) return;

                            // CR 701.21 — Exile. Routed through the owning
                            // player's zones so owner-of-zone bookkeeping
                            // stays consistent across multi-player games
                            // (mirrors Anguished Unmaking / Path to Exile).
                            // Indestructible (CR 702.12) does not prevent
                            // exile.
                            var fromOwner = target.Owner;
                            if (fromOwner != null)
                            {
                                fromOwner.Zones.Battlefield.RemoveCard(target);
                                fromOwner.Zones.Exile.AddCard(target);
                            }
                            target.SetZone(ZoneType.Exile);
                        }),
                };
            });
    }
}
