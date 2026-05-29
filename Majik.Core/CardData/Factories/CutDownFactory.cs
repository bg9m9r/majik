using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Cut Down (Dominaria United, {B}).
///
/// Instant. Oracle text:
///   "Destroy target creature with total power and toughness 5 or less."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}, owner / controller.
/// - <b>Destroy target creature with total power and toughness 5 or less</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/> with a
///   single 1..1 target <see cref="TargetRequest"/>. On resolution the chosen
///   creature's effective power + toughness is summed (CR 208 / 302.4 — power
///   and toughness, including any continuous effects in force at resolution via
///   <see cref="Creature.Power"/> / <see cref="Creature.Toughness"/>) and the
///   creature is destroyed via <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) iff it is still a Creature on the Battlefield (CR 608.2b —
///   illegal target at resolution → no-op) whose total is 5 or less.
///
/// "Total power and toughness 5 or less" sums the creature's current power and
/// its current toughness; the threshold is inclusive (≤ 5). A creature with
/// negative power/toughness still counts (e.g. a -1/-1'd 3/3 is 2+2 = 4).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate — same
/// posture as <see cref="DoomBladeFactory"/> / <see cref="GoForTheThroatFactory"/>.
/// </summary>
[CardName("Cut Down")]
public static class CutDownFactory
{
    public const string CardName = "Cut Down";
    public const string PrintedManaCost = "{B}";

    /// <summary>Maximum total power + toughness of a legal target (≤ 5).</summary>
    private const int MaxTotalPowerToughness = 5;

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy target
    /// creature with total power and toughness 5 or less) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target creature with total power and toughness 5 or
    /// less" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a Creature on the Battlefield
    /// AND has effective power + toughness ≤ 5 (CR 608.2b — illegal-target
    /// filter at resolution). When valid, destroys the target via
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
                    Description: "target creature with total power and toughness 5 or less",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every creature on any
                    // battlefield whose current power + toughness is ≤ 5.
                    // Removal intent in the bot's ranker pushes the opponent's
                    // biggest legal threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.Power + c.Toughness <= MaxTotalPowerToughness)
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
                        $"{CardName}: destroy target creature with total power and toughness 5 or less",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 208 / 302.4 — sum the creature's current power
                            // and toughness; the ≤ 5 threshold is inclusive.
                            if (target.Power + target.Toughness > MaxTotalPowerToughness) return;

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
