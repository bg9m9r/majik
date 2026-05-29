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
/// Named-card factory for Power Word Kill (Adventures in the Forgotten
/// Realms, {1}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target non-Angel, non-Demon, non-Devil, non-Dragon creature."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, owner / controller.
/// - <b>Destroy target creature with none of the four excluded subtypes</b>
///   — <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 target request. On resolution the chosen creature is
///   filtered via <see cref="Card.HasSubtype(CardSubtype)"/> (CR 205.3m —
///   creature subtypes) and destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7) iff it is
///   still a Creature on the Battlefield AND carries none of Angel / Demon /
///   Devil / Dragon (CR 608.2b — illegal target at resolution → no-op).
///
/// Same posture as its cycle-mate <see cref="GoForTheThroatFactory"/> — a
/// destroy-target-creature instant whose targeting restriction is enforced as
/// a resolution-time legality re-check (CR 608.2b). The declarative JSON
/// effect schema only ships a non-functional <c>destroy_target_stub</c>, so
/// the working analogue is the code-based <see cref="SpellDefinition"/> path,
/// not <see cref="CardDefinitionLoader.FromEmbeddedResource"/>.
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate.
/// </summary>
[CardName("Power Word Kill")]
public static class PowerWordKillFactory
{
    public const string CardName = "Power Word Kill";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>The creature subtypes Power Word Kill cannot target
    /// (CR 205.3m). Mirrors the printed "non-Angel, non-Demon, non-Devil,
    /// non-Dragon" restriction.</summary>
    private static readonly CardSubtype[] ExcludedSubtypes =
    {
        CardSubtype.Angel,
        CardSubtype.Demon,
        CardSubtype.Devil,
        CardSubtype.Dragon,
    };

    /// <summary>CardDef DSL — card shape only. Resolve behaviour is built on
    /// demand via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    private static bool IsLegalTarget(Creature c) =>
        !ExcludedSubtypes.Any(c.HasSubtype);

    /// <summary>
    /// Build the "destroy target non-Angel, non-Demon, non-Devil, non-Dragon
    /// creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a Creature on the Battlefield
    /// AND carries none of the four excluded subtypes (CR 608.2b — illegal
    /// target filter at resolution). When valid, destroys the target via
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
                    Description: "target non-Angel, non-Demon, non-Devil, non-Dragon creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every legal (non-excluded)
                    // creature on any battlefield. Removal intent pushes the
                    // opponent's biggest legal threat up in the bot's ranker.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(IsLegalTarget)
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
                        $"{CardName}: destroy target non-Angel, non-Demon, non-Devil, non-Dragon creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 205.3m — reject the four excluded subtypes.
                            if (!IsLegalTarget(target)) return;

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
