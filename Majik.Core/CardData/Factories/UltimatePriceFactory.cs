using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Ultimate Price (Magic Origins, {1}{B}).
///
/// Instant. Oracle text:
///   "Destroy target monocolored creature."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, owner / controller.
/// - <b>Destroy target monocolored creature</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single 1..1
///   "target monocolored creature" <see cref="TargetRequest"/>. On resolution
///   the chosen creature is filtered via
///   <see cref="Majik.Core.Cards.CardColors.GetColors"/> (CR 105 — monocolored
///   means exactly one colour in the colour set; colourless = 0 colours ≠ legal;
///   multicolour = ≥2 colours ≠ legal) and destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) iff it is still a Creature on the Battlefield (CR 608.2b —
///   illegal target at resolution → no-op).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate — same
/// posture as <see cref="DoomBladeFactory"/> / <see cref="TerminateFactory"/>.
/// </summary>
[CardName("Ultimate Price")]
public static class UltimatePriceFactory
{
    public const string CardName = "Ultimate Price";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target monocolored creature) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target monocolored creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// AND is monocolored (exactly one colour, CR 105 — CR 608.2b illegal-target
    /// filter at resolution).
    /// When valid, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
    /// indestructible / regeneration shields are honoured at the destroy site.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
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
                    Description: "target monocolored creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every monocolored creature on
                    // any battlefield. Monocolored = exactly 1 colour (CR 105).
                    // Colorless (0 colours) and multicolor (≥2 colours) are NOT legal.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
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
                        $"{CardName}: destroy target monocolored creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 105 — monocolored filter: exactly one colour.
                            // Colourless (Count == 0) and multicolour (Count >= 2)
                            // are not monocolored and are skipped.
                            if (CardColors.GetColors(target).Count != 1) return;

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
