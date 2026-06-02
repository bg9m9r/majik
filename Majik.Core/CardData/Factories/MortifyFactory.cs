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
/// Instant. Oracle text (verified against Scryfall 2026-06-02):
///   "Destroy target creature or enchantment."
///
/// Mortify is the white-black "destroy target creature or enchantment"
/// instant — structurally the creature-or-X destroy twin of
/// <see cref="HerosDownfallFactory"/> / <see cref="DreadboreFactory"/>
/// (creature or planeswalker): the "destroy target ... or ..." resolve and
/// 1..1 target request are reused verbatim; only the second allowed card
/// type differs (Enchantment rather than Planeswalker).
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{W}{B}. The base shape (name /
///   Instant type / {1}{W}{B} cost) is materialised from the embedded JSON
///   definition (<c>mortify.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="DreadboreFactory"/> (the JSON <c>SpellDefinition</c> schema
///   does not yet express a creature-or-enchantment target request, so the
///   resolve behaviour is layered on here via <see cref="BuildDefinition"/>).
/// - <b>Destroy target creature or enchantment</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature or enchantment"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding cards with
///   <see cref="CardType.Creature"/> or <see cref="CardType.Enchantment"/>
///   (CR 700.4 — a permanent may have multiple card types). The bot's
///   <see cref="BotIntent.Removal"/> ranker pushes opponent permanents
///   to the top.
/// - On resolution: re-checks the target is still a Creature or
///   Enchantment on the Battlefield (CR 608.2b illegal-target gate),
///   then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// Destroy-reason gate — same posture as <see cref="DreadboreFactory"/>
/// / <see cref="HerosDownfallFactory"/>.
/// </summary>
[CardName("Mortify")]
public static class MortifyFactory
{
    public const string CardName = "Mortify";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "mortify";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {1}{W}{B}) from
    /// the embedded JSON definition. Resolve behaviour (destroy target
    /// creature or enchantment) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="DreadboreFactory"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Instant card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as an Instant but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "destroy target creature or enchantment"
    /// <see cref="SpellDefinition"/>. On resolve: validates the target is
    /// still a Creature or Enchantment on the Battlefield (CR 608.2b —
    /// illegal target → no-op); when valid, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
    /// indestructible / regeneration shields are honoured at the destroy
    /// site. Identical structure to Hero's Downfall / Dreadbore — Mortify
    /// differs only in the second allowed type (Enchantment vs Planeswalker).
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
                    Description: "target creature or enchantment",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every creature /
                    // enchantment on any battlefield. Removal intent in
                    // the bot's ranker pushes opponent permanents up.
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
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Enchantment)) return;

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
