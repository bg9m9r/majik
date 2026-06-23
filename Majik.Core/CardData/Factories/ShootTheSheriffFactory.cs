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
/// Named-card factory for Shoot the Sheriff (Outlaws of Thunder Junction, {1}{B}).
///
/// Instant. Oracle text:
///   "Destroy target non-outlaw creature. (Assassins, Mercenaries, Pirates,
///    Rogues, and Warlocks are outlaws. Everyone else is fair game.)"
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, owner / controller.
/// - <b>Destroy target non-outlaw creature</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single 1..1 "target
///   non-outlaw creature" <see cref="TargetRequest"/>. On resolution the chosen
///   creature is filtered against the "outlaw" creature-type set (CR 205.3m —
///   creature subtypes: Assassin, Mercenary, Pirate, Rogue, Warlock; the
///   "outlaw" group is the official OTJ shorthand for those five subtypes) and
///   destroyed via <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7)
///   iff it is still a Creature on the Battlefield and is NOT an outlaw
///   (CR 608.2b — illegal target at resolution → no-op).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate — same
/// posture as <see cref="EyeblightsEndingFactory"/> / <see cref="CastDownFactory"/>.
///
/// Outlaw creatures (e.g. Ragavan, Nimble Pilferer — a Monkey Pirate) are
/// excluded: the non-outlaw filter rejects any target whose subtype list
/// contains one of the five outlaw subtypes.
/// </summary>
[CardName("Shoot the Sheriff")]
public static class ShootTheSheriffFactory
{
    public const string CardName = "Shoot the Sheriff";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>The five creature subtypes that make a creature an "outlaw"
    /// (Outlaws of Thunder Junction reminder text; CR 205.3m subtypes).</summary>
    private static readonly CardSubtype[] OutlawSubtypes =
    {
        CardSubtype.Assassin,
        CardSubtype.Mercenary,
        CardSubtype.Pirate,
        CardSubtype.Rogue,
        CardSubtype.Warlock,
    };

    private static bool IsOutlaw(Creature creature)
    {
        foreach (var subtype in OutlawSubtypes)
        {
            if (creature.HasSubtype(subtype)) return true;
        }

        return false;
    }

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target non-outlaw creature) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target non-outlaw creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a Creature on the Battlefield
    /// AND is not an outlaw (CR 608.2b — illegal-target filter at resolution).
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
                    Description: "target non-outlaw creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every non-outlaw creature on
                    // any battlefield. Removal intent in the bot's ranker pushes
                    // the opponent's biggest eligible threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !IsOutlaw(c))
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
                        $"{CardName}: destroy target non-outlaw creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 205.3m — non-outlaw filter: a creature with any
                            // of the five outlaw subtypes is an illegal target.
                            if (IsOutlaw(target)) return;

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
