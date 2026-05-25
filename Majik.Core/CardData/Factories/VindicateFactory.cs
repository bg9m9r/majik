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
/// Named-card factory for Vindicate (Apocalypse, {W}{B}{B}).
///
/// Sorcery. Oracle text:
///   "Destroy target permanent."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {W}{B}{B}, owner / controller.
/// - <b>Destroy target permanent</b> — <see cref="BuildDefinition"/>
///   returns a <see cref="SpellDefinition"/> with a single 1..1
///   "target permanent" <see cref="TargetRequest"/>. The live
///   <c>CandidateGatherer</c> walks every player's battlefield, yielding
///   <i>every</i> permanent (any type — creatures, artifacts, enchantments,
///   lands, planeswalkers, battles). Vindicate notably hits lands, unlike
///   <see cref="HerosDownfallFactory"/> (creature/PW only) or
///   <see cref="AnguishedUnmakingFactory"/> (nonland permanent).
/// - On resolution: re-checks the target is still a Permanent on the
///   Battlefield (CR 608.2b illegal-target gate), then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by the Destroy-reason gate — same posture as
/// <see cref="BeastWithinFactory"/> (the v1 reference for "destroy target
/// permanent" wording).
/// </summary>
[CardName("Vindicate")]
public static class VindicateFactory
{
    public const string CardName = "Vindicate";
    public const string PrintedManaCost = "{W}{B}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target permanent) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target permanent" <see cref="SpellDefinition"/>.
    /// On resolve: validates the target is still a Permanent on the
    /// Battlefield (CR 608.2b — illegal target → no-op); when valid,
    /// destroys via <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
    /// indestructible / regeneration shields are honoured at the destroy
    /// site.
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
                    Description: "target permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every permanent on any
                    // battlefield (any card type — CR 110.1). Removal
                    // intent + ownership flip pushes the opponent's most
                    // valuable permanent to the top.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
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
                        $"{CardName}: destroy target permanent",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
