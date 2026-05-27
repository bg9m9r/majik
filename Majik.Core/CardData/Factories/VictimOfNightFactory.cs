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
/// Named-card factory for Victim of Night (Innistrad, {B}{B}).
///
/// Instant. Oracle text:
///   "Destroy target non-Vampire, non-Werewolf, non-Zombie creature."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}{B}, owner / controller.
/// - <b>Destroy target non-Vampire, non-Werewolf, non-Zombie creature</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 target request. On resolution the chosen creature
///   is checked for Vampire, Werewolf, or Zombie subtypes (CR 205.3m —
///   creature subtypes) and destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) iff it is still a Creature on the Battlefield and none of
///   those subtypes are present (CR 608.2b — illegal target at resolution
///   → no-op).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate —
/// same posture as <see cref="DoomBladeFactory"/> / <see cref="TerminateFactory"/>.
/// </summary>
[CardName("Victim of Night")]
public static class VictimOfNightFactory
{
    public const string CardName = "Victim of Night";
    public const string PrintedManaCost = "{B}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target non-Vampire/Werewolf/Zombie creature) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target non-Vampire, non-Werewolf, non-Zombie
    /// creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// AND has none of the three excluded subtypes (CR 608.2b — illegal-
    /// target filter at resolution).  When valid, destroys the target via
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
                    Description: "target non-Vampire, non-Werewolf, non-Zombie creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every eligible creature on
                    // any battlefield. Removal intent in the bot's ranker
                    // pushes the opponent's biggest eligible threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c =>
                            !c.HasSubtype(CardSubtype.Vampire) &&
                            !c.HasSubtype(CardSubtype.Werewolf) &&
                            !c.HasSubtype(CardSubtype.Zombie))
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
                        $"{CardName}: destroy target non-Vampire, non-Werewolf, non-Zombie creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 205.3m — subtype filter: Vampire, Werewolf,
                            // and Zombie are all illegal targets.
                            if (target.HasSubtype(CardSubtype.Vampire)) return;
                            if (target.HasSubtype(CardSubtype.Werewolf)) return;
                            if (target.HasSubtype(CardSubtype.Zombie)) return;

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
