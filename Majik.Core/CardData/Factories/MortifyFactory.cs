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
/// Named-card factory for Mortify (Guildpact, {1}{W}{B}).
///
/// Instant. Oracle text (verified against Scryfall):
///   "Destroy target creature or enchantment."
///
/// ## Implementation
///
/// Card shape comes from the embedded JSON (<c>mortify.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
/// <see cref="CardDefinitionFactory"/> (same data-only shape as
/// <see cref="PutrefyFactory"/>). The resolve-time body lives in
/// <see cref="BuildDefinition"/> because a <see cref="SpellDefinition"/>
/// needs a target resolver supplied by the caller's
/// <see cref="GameContext"/> (not expressible in the data-only JSON schema).
///
/// - <b>Destroy target creature or enchantment</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature or enchantment"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding permanents that have type
///   Creature (CR 302) or type Enchantment (CR 303). This mirrors
///   <see cref="PutrefyFactory"/>'s artifact-or-creature predicate, narrowed
///   to the creature-or-enchantment clause (an enchantment creature, such as
///   a God or a bestowed Aura, satisfies both clauses).
///   On resolution it re-checks the target is still a Permanent on the
///   Battlefield with type Creature or Enchantment (CR 608.2b illegal-target
///   gate), then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>.
/// - Plain "destroy" (no "can't be regenerated" rider) — the destroy uses
///   <see cref="ZoneMoveReason.Destroy"/> (mirrors
///   <see cref="HerosDownfallFactory"/> / <see cref="VindicateFactory"/>):
///   indestructible (CR 702.12) cancels the destroy and any active
///   regeneration shield (CR 701.15) is honoured at the destroy site.
/// </summary>
[CardName("Mortify")]
public static class MortifyFactory
{
    public const string CardName = "Mortify";
    public const string Slug = "mortify";
    public const string PrintedManaCost = "{1}{W}{B}";

    /// <summary>Build the card shape from the embedded JSON definition.</summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        return (Instant)CardDefinitionFactory.Build(def, owner);
    }

    /// <summary>
    /// Build the "destroy target creature or enchantment"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the resolved target is still a
    /// <see cref="Permanent"/> on the Battlefield AND has type
    /// <see cref="CardType.Creature"/> or <see cref="CardType.Enchantment"/>
    /// (CR 608.2b — illegal target at resolution → no-op); then destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
    /// (CR 702.12) cancels the destroy and any regeneration shield
    /// (CR 701.15) is honoured. Mirrors <see cref="PutrefyFactory"/>'s
    /// destroy gather, narrowed to the creature-or-enchantment clause, with
    /// the plain-destroy posture of <see cref="HerosDownfallFactory"/>.
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
                    Description: "target creature or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield permanents
                    // that are creatures (CR 302) or enchantments (CR 303).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                                 || c.HasType(CardType.Enchantment))
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
                        $"{CardName}: destroy target creature or enchantment",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must be a creature or
                            // enchantment at resolution (CR 608.2b).
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Enchantment)) return;

                            // CR 701.7 — Destroy. Plain destroy: indestructible
                            // (CR 702.12) cancels it and any regeneration shield
                            // (CR 701.15) is honoured via the Destroy-reason gate
                            // in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
