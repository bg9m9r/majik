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
/// Named-card factory for Dreadbore (Return to Ravnica, {B}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-05-29):
///   "Destroy target creature or planeswalker."
///
/// Dreadbore is the {B}{R} <b>sorcery</b> twin of
/// <see cref="HerosDownfallFactory"/> (Hero's Downfall, the {1}{B}{B}
/// instant): identical "destroy target creature or planeswalker" effect,
/// dropped to sorcery timing. The creature-or-planeswalker
/// <see cref="TargetRequest"/> + Destroy resolve are reused verbatim; only
/// the card shape (Sorcery / {B}{R}) differs.
///
/// ## Implemented (v1)
/// - <b>Sorcery shape</b> at printed cost {B}{R}. The base shape (name /
///   Sorcery type / {B}{R} cost) is materialised from the embedded JSON
///   definition (<c>dreadbore.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="ArdentPleaFactory"/> (the JSON <c>SpellDefinition</c> schema
///   does not yet express a creature-or-planeswalker target request, so the
///   resolve behaviour is layered on here via <see cref="BuildDefinition"/>).
/// - <b>Destroy target creature or planeswalker</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature or planeswalker"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding cards with
///   <see cref="CardType.Creature"/> or <see cref="CardType.Planeswalker"/>
///   (CR 700.4 — a permanent may have multiple card types). The bot's
///   <see cref="BotIntent.Removal"/> ranker pushes opponent permanents
///   to the top.
/// - On resolution: re-checks the target is still a Creature or
///   Planeswalker on the Battlefield (CR 608.2b illegal-target gate),
///   then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7).
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are
/// honoured by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// Destroy-reason gate — same posture as <see cref="HerosDownfallFactory"/>
/// / <see cref="TerminateFactory"/>.
/// </summary>
[CardName("Dreadbore")]
public static class DreadboreFactory
{
    public const string CardName = "Dreadbore";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "dreadbore";

    /// <summary>
    /// Materialise the Sorcery card shape (name / Sorcery / {B}{R}) from the
    /// embedded JSON definition. Resolve behaviour (destroy target creature
    /// or planeswalker) is built on demand via <see cref="BuildDefinition"/>,
    /// mirroring <see cref="HerosDownfallFactory"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var built = CardDefinitionFactory.Build(definition, owner);
        if (built is not Sorcery card)
        {
            throw new InvalidOperationException(
                $"Expected '{CardName}' to materialise as a Sorcery but got "
                + $"'{built.GetType().Name}'.");
        }

        return card;
    }

    /// <summary>
    /// Build the "destroy target creature or planeswalker"
    /// <see cref="SpellDefinition"/>. On resolve: validates the target is
    /// still a Creature or Planeswalker on the Battlefield (CR 608.2b —
    /// illegal target → no-op); when valid, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
    /// indestructible / regeneration shields are honoured at the destroy
    /// site. Identical to Hero's Downfall — Dreadbore differs only in timing
    /// (sorcery vs instant), which is a casting-restriction concern handled
    /// by the Sorcery card shape, not the resolve.
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
                    Description: "target creature or planeswalker",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every creature /
                    // planeswalker on any battlefield. Removal intent in
                    // the bot's ranker pushes opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature)
                            || c.HasType(CardType.Planeswalker))
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
                        $"{CardName}: destroy target creature or planeswalker",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Planeswalker)) return;

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
