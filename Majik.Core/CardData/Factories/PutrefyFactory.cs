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
/// Named-card factory for Putrefy (Ravnica: City of Guilds, {1}{B}{G}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target artifact or creature. It can't be regenerated."
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>putrefy.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> (same data-only shape as
/// <see cref="AncientGrudgeFactory"/>). The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
///
/// - <b>Destroy target artifact or creature</b> — <see cref="BuildDefinition"/>
///   returns a <see cref="SpellDefinition"/> with a single 1..1
///   "target artifact or creature" <see cref="TargetRequest"/>. The live
///   <c>CandidateGatherer</c> walks every player's battlefield, yielding
///   permanents that have type Artifact or type Creature (CR 301 / CR 302).
///   This widens <see cref="AncientGrudgeFactory"/>'s artifact-only predicate
///   to the artifact-or-creature predicate (also covers artifact creatures,
///   which satisfy both clauses).
///   On resolution it re-checks the target is still a Permanent on the
///   Battlefield with type Artifact or Creature (CR 608.2b illegal-target
///   gate), then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>.
/// - <b>"It can't be regenerated"</b> (CR 701.15) — the destroy uses
///   <see cref="ZoneMoveReason.DestroyNoRegeneration"/> (mirrors
///   <see cref="TerminateFactory"/>): indestructible (CR 702.12) still
///   cancels the destroy, but any active regeneration shield is bypassed
///   rather than consumed.
/// </summary>
[CardName("Putrefy")]
public static class PutrefyFactory
{
    public const string CardName = "Putrefy";
    public const string Slug = "putrefy";
    public const string PrintedManaCost = "{1}{B}{G}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "destroy target artifact or creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the resolved target is still a
    /// <see cref="Permanent"/> on the Battlefield AND has type
    /// <see cref="CardType.Artifact"/> or <see cref="CardType.Creature"/>
    /// (CR 608.2b — illegal target at resolution → no-op); then destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.DestroyNoRegeneration"/> (CR 701.7) so
    /// indestructible (CR 702.12) cancels the destroy while the
    /// "can't be regenerated" rider (CR 701.15) bypasses any regeneration
    /// shield. Mirrors <see cref="AncientGrudgeFactory"/>'s destroy gather +
    /// <see cref="TerminateFactory"/>'s no-regeneration rider.
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
                    Description: "target artifact or creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are artifacts (CR 301) or creatures (CR 302).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Artifact)
                                 || c.HasType(CardType.Creature))
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
                        $"{CardName}: destroy target artifact or creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must be an artifact or
                            // creature at resolution (CR 608.2b).
                            if (!target.HasType(CardType.Artifact)
                                && !target.HasType(CardType.Creature)) return;

                            // CR 701.7 — Destroy. "It can't be regenerated"
                            // (CR 701.15) honoured via DestroyNoRegeneration:
                            // indestructible (CR 702.12) still cancels the
                            // destroy, but any active regeneration shield is
                            // bypassed rather than consumed.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.DestroyNoRegeneration);
                        }),
                };
            });
    }
}
