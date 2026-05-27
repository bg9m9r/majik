using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eyeblight's Ending (Lorwyn, {2}{B}).
///
/// Instant. Oracle text:
///   "Destroy target non-Elf creature."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{B}, owner / controller.
/// - <b>Destroy target non-Elf creature</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single 1..1
///   "target non-Elf creature" <see cref="TargetRequest"/>. On resolution
///   the chosen creature is checked for the Elf subtype (CR 205.3m —
///   creature subtypes) and destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) iff it is still a Creature on the Battlefield and does not
///   have the Elf subtype (CR 608.2b — illegal target at resolution → no-op).
///
/// Note: Eyeblight's Ending is printed as "Kindred Instant — Elf" but the
/// Kindred/Elf typing is cosmetic in this engine; it is treated as a plain
/// black Instant.
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate — same
/// posture as <see cref="VictimOfNightFactory"/> / <see cref="DoomBladeFactory"/>.
/// </summary>
[CardName("Eyeblight's Ending")]
public static class EyeblightsEndingFactory
{
    public const string CardName = "Eyeblight's Ending";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target non-Elf creature) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target non-Elf creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// AND does not have the Elf subtype (CR 608.2b — illegal-target filter
    /// at resolution).  When valid, destroys the target via
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
                    Description: "target non-Elf creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every non-Elf creature on
                    // any battlefield. Removal intent in the bot's ranker
                    // pushes the opponent's biggest eligible threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !c.HasSubtype(CardSubtype.Elf))
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
                        $"{CardName}: destroy target non-Elf creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 205.3m — subtype filter: Elf is an illegal target.
                            if (target.HasSubtype(CardSubtype.Elf)) return;

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
