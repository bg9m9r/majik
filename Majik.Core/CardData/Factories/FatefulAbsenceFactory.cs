using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Fateful Absence (Innistrad: Midnight Hunt, {1}{W}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-02):
///   "Destroy target creature or planeswalker. Its controller investigates.
///    (Create a Clue token. It's an artifact with '{2}, Sacrifice this
///    token: Draw a card.')"
///
/// Fateful Absence is premium white removal: a two-mana instant that
/// destroys any creature or planeswalker, with the downside rider that the
/// destroyed permanent's controller investigates (banks a Clue). It fuses
/// the <see cref="DreadboreFactory"/> / <see cref="HerosDownfallFactory"/>
/// "destroy target creature or planeswalker" resolve (CR 701.7) with the
/// shared Clue primitive used by <see cref="ThrabenInspectorFactory"/> /
/// Bygone Bishop / Tireless Tracker (CR 701.39, Investigate).
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {1}{W}. The base shape (name /
///   Instant type / {1}{W} cost) is materialised from the embedded JSON
///   definition (<c>fateful-absence.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="DreadboreFactory"/> (the JSON <c>SpellDefinition</c> schema
///   does not yet express a creature-or-planeswalker target request, so the
///   resolve behaviour is layered on here via <see cref="BuildDefinition"/>).
/// - <b>Destroy target creature or planeswalker</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature or planeswalker"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding cards with
///   <see cref="CardType.Creature"/> or <see cref="CardType.Planeswalker"/>
///   (CR 700.4 — a permanent may have multiple card types). The bot's
///   <see cref="BotIntent.Removal"/> ranker pushes opponent permanents up.
/// - <b>Its controller investigates</b> (CR 701.39): on resolution the
///   controller of the targeted permanent is captured <i>before</i> the
///   destroy (the card retains its <see cref="Permanent.Controller"/> after
///   leaving the battlefield, but capturing first keeps the intent explicit),
///   then a single Clue token is created under that controller via the
///   shared <see cref="TokenFactory.CreateClue"/> helper.
///
/// ## Resolution legality
/// On resolve the target is re-checked as a Creature or Planeswalker on the
/// Battlefield (CR 608.2b illegal-target gate). If the target is illegal at
/// resolution the spell does nothing — including no investigate, because
/// "Its controller investigates" is part of the same one-shot effect tied to
/// the (now illegal) target (CR 608.2c — the whole instruction is skipped).
/// When valid, the target is destroyed via
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
/// indestructible (CR 702.12) / regeneration (CR 701.15) shields are
/// honoured, then the controller investigates.
/// </summary>
[CardName("Fateful Absence")]
public static class FatefulAbsenceFactory
{
    public const string CardName = "Fateful Absence";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "fateful-absence";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {1}{W}) from the
    /// embedded JSON definition. Resolve behaviour (destroy + investigate) is
    /// built on demand via <see cref="BuildDefinition"/>, mirroring
    /// <see cref="DreadboreFactory"/>.
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
    /// Build the "destroy target creature or planeswalker; its controller
    /// investigates" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the target is still a Creature or Planeswalker on
    /// the Battlefield (CR 608.2b — illegal target → no-op, no investigate);
    /// when valid, captures the controller, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7), then
    /// that controller investigates (CR 701.39 — one Clue token via
    /// <see cref="TokenFactory.CreateClue"/>).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <param name="zoneService">When supplied, the Clue token is placed onto
    /// the battlefield via the ZoneService so its arrival event fires. Pass
    /// <c>null</c> for shape / dispatcher tests.</param>
    public static SpellDefinition BuildDefinition(
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
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
                    // planeswalker on any battlefield. Removal intent in the
                    // bot's ranker pushes opponent permanents up.
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
                        $"{CardName}: destroy target creature or planeswalker; "
                            + "its controller investigates",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // Illegal target → the whole instruction (destroy
                            // AND investigate) is skipped (CR 608.2c).
                            if (resolved is not Permanent target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            if (!target.HasType(CardType.Creature)
                                && !target.HasType(CardType.Planeswalker)) return;

                            // "Its controller" — capture before the destroy so
                            // the investigate is attributed to the permanent's
                            // controller at resolution.
                            var controller = target.Controller ?? target.Owner;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);

                            // "Its controller investigates." CR 701.39 —
                            // create one Clue token under that controller.
                            if (controller is not null)
                            {
                                TokenFactory.CreateClue(controller, zoneService);
                            }
                        }),
                };
            });
    }
}
