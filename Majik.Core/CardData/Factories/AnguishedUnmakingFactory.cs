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
/// Named-card factory for Anguished Unmaking (Shadows over Innistrad,
/// {1}{W}{B}).
///
/// Instant. Oracle text:
///   "Exile target nonland permanent. You lose 3 life."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{W}{B}, owner / controller.
/// - <b>Exile target nonland permanent</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target nonland permanent" <see cref="TargetRequest"/>.
///   The live <c>CandidateGatherer</c> walks every player's battlefield,
///   yielding permanents whose card-type set does NOT include
///   <see cref="CardType.Land"/> (CR 305 — Lands are a card type, not a
///   subtype, so the filter rejects e.g. Dryad Arbor too).
/// - On resolution: re-checks the target is still a non-land permanent on
///   the Battlefield (CR 608.2b illegal-target gate); when valid, the
///   target is exiled (CR 701.21) by routing through the owning player's
///   zones (mirrors <see cref="PrismaticEndingFactory"/> / Path to Exile).
///   Indestructible (CR 702.12) does NOT prevent exile — the card moves
///   regardless.
/// - <b>You lose 3 life</b> — the caster loses 3 life via
///   <see cref="Player.LoseLife"/> (CR 119.3). The life loss is
///   <i>unconditional</i> per the printed oracle text (two consecutive
///   sentences with no conditional gate) and fires even when the exile
///   half fizzles on an illegal target — same posture as Swift End's
///   life-loss clause (<see cref="MurderousRiderFactory.BuildAdventureSpell"/>).
/// </summary>
[CardName("Anguished Unmaking")]
public static class AnguishedUnmakingFactory
{
    public const string CardName = "Anguished Unmaking";
    public const string PrintedManaCost = "{1}{W}{B}";

    /// <summary>Life the caster pays on resolution (printed value).</summary>
    public const int CasterLifeLoss = 3;

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (exile target nonland permanent + caster loses 3 life) is built on
    /// demand via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "exile target nonland permanent; you lose 3 life"
    /// <see cref="SpellDefinition"/>. On resolve:
    /// <list type="number">
    ///   <item>CR 608.2b re-check: target must still be on the Battlefield
    ///     and must not be a Land card type.</item>
    ///   <item>CR 701.21 — exile the target via owner-routed zone moves
    ///     (same surface as <see cref="PrismaticEndingFactory"/>).</item>
    ///   <item>CR 119.3 — caster loses
    ///     <see cref="CasterLifeLoss"/> life. Fires <i>even if</i> the
    ///     exile half fizzles (printed wording is two separate
    ///     sentences).</item>
    /// </list>
    /// </summary>
    /// <param name="caster">The controller of Anguished Unmaking — the
    /// player who pays the 3 life on resolve.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target
    /// token to the live engine object. Pass <c>o =&gt; o</c> for tests
    /// that hand permanents directly.</param>
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
                    Description: "target nonland permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every nonland permanent
                    // on any battlefield. Removal intent in the bot's
                    // ranker pushes opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => !c.HasType(CardType.Land))
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
                        $"{CardName}: exile target nonland permanent; you lose 3 life",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // Exile only fires when the target is still a
                            // nonland permanent on the battlefield.
                            if (resolved is Permanent target
                                && target.Zone == ZoneType.Battlefield
                                && !target.HasType(CardType.Land))
                            {
                                // CR 701.21 — Exile. Routed through the
                                // owning player's zones so owner-of-zone
                                // bookkeeping stays consistent across
                                // multi-player games (mirrors PathToExile /
                                // PrismaticEnding). Indestructible
                                // (CR 702.12) does not prevent exile.
                                var fromOwner = target.Owner;
                                if (fromOwner != null)
                                {
                                    fromOwner.Zones.Battlefield.RemoveCard(target);
                                    fromOwner.Zones.Exile.AddCard(target);
                                }
                                target.SetZone(ZoneType.Exile);
                            }

                            // CR 119.3 — caster loses 3 life as part of
                            // the same resolution. Pay even when the exile
                            // half fizzles on an illegal target (printed
                            // wording is two consecutive sentences with no
                            // conditional gate).
                            caster.LoseLife(CasterLifeLoss);
                        }),
                };
            });
    }
}
