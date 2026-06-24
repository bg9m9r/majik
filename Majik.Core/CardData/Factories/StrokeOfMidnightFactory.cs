using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stroke of Midnight (Throne of Eldraine, {2}{W}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-24):
///   "Destroy target nonland permanent. Its controller creates a 1/1 white
///    Human creature token."
///
/// ## Why it gets its own factory
/// The destroy half is the same "destroy target nonland permanent" body as
/// <see cref="AnguishedUnmakingFactory"/> (which exiles rather than destroys);
/// the rider is the "its controller creates a 1/1 white Human creature token"
/// clause, minted via <see cref="TokenFactory.CreateOnBattlefield"/> using the
/// same 1/1 white Human <c>TokenSpec</c> as
/// <see cref="GatherTheTownsfolkFactory"/>. Both primitives already ship — no
/// new engine mechanic is required.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {2}{W}, white (CR 105.2 / 202.2a),
///   mana value 3 (CR 202.3). Base shape (name / Instant type / {2}{W} cost) is
///   materialised from the embedded JSON definition
///   (<c>stroke-of-midnight.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="MortifyFactory"/> (the JSON SpellDefinition schema does not yet
///   express a target request, so resolve behaviour is layered on here via
///   <see cref="BuildDefinition"/>).
/// - <b>Destroy target nonland permanent</b> —
///   <see cref="BuildDefinition"/> returns a <see cref="SpellDefinition"/> with
///   a single 1..1 "target nonland permanent" <see cref="TargetRequest"/>. The
///   live <c>CandidateGatherer</c> walks every player's battlefield, yielding
///   permanents whose card-type set does NOT include
///   <see cref="CardType.Land"/> (CR 305 — Land is a card type, so the filter
///   also rejects e.g. Dryad Arbor).
/// - On resolution: re-checks the target is still a nonland permanent on the
///   Battlefield (CR 608.2b illegal-target gate), then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
///   indestructible (CR 702.12) / regeneration (CR 701.15) shields are honoured.
/// - <b>"Its controller creates a 1/1 white Human creature token"</b> — the
///   token is created under the destroyed permanent's <i>controller</i>
///   (CR 109.5 / the printed "its controller", NOT the caster). The controller
///   reference is snapshotted BEFORE the destroy moves the card off the
///   battlefield. Per the printed wording the two clauses are independent
///   sentences with no conditional gate: the token is created even when the
///   destroy half fizzles on an illegal target — in that fizzle case "its
///   controller" is read from the (still-resolvable) target permanent's
///   current controller (CR 608.2b leaves the rest of the effect to resolve as
///   much as possible).
///
/// ## Rules citations
/// - CR 608.2 / 608.2b — one-shot resolution + illegal-target re-check.
/// - CR 701.7 — Destroy (indestructible / regeneration honoured at the site).
/// - CR 111 / 111.4 — create a 1/1 white Human creature token.
/// - CR 109.5 — "its controller" refers to the target permanent's controller.
/// </summary>
[CardName("Stroke of Midnight")]
public static class StrokeOfMidnightFactory
{
    public const string CardName = "Stroke of Midnight";
    public const string Slug = "stroke-of-midnight";
    public const string PrintedManaCost = "{2}{W}";

    public const int TokenPower = 1;
    public const int TokenToughness = 1;

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {2}{W}) from the
    /// embedded JSON definition. Resolve behaviour (destroy target nonland
    /// permanent + token rider) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="MortifyFactory"/>.
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
    /// Build the "destroy target nonland permanent; its controller creates a
    /// 1/1 white Human creature token" <see cref="SpellDefinition"/>. On
    /// resolve: snapshot the target's controller, destroy the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7), then
    /// mint a 1/1 white Human token under that controller (CR 111.4).
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <param name="zoneService">Optional zone service — routes the token's ETB
    /// through <see cref="ZoneService"/> so
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> publishes (enabling
    /// downstream ETB triggers). Null → direct zone move.</param>
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
                    Description: "target nonland permanent",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every nonland permanent on
                    // any battlefield. Removal intent in the bot's ranker
                    // pushes opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => !c.HasType(CardType.Land))
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
                        $"{CardName}: destroy target nonland permanent; its controller creates a 1/1 white Human token",
                        () =>
                        {
                            if (resolved is not Permanent target) return;

                            // CR 109.5 / printed "its controller" — snapshot the
                            // controller BEFORE the destroy moves the card off
                            // the battlefield (where its controller would clear).
                            var tokenController = target.Controller;

                            // CR 608.2b — resolution-time legality re-check.
                            // Destroy only fires when the target is still a
                            // nonland permanent on the battlefield.
                            if (target.Zone == ZoneType.Battlefield
                                && !target.HasType(CardType.Land))
                            {
                                // CR 701.7 — Destroy. Indestructible
                                // (CR 702.12) / regeneration (CR 701.15) handled
                                // by the Destroy-reason gate in MoveToGraveyard.
                                OracleSpellBinder.MoveToGraveyard(
                                    target,
                                    Majik.Core.Zones.ZoneMoveReason.Destroy);
                            }

                            // CR 111.4 — "its controller creates a 1/1 white
                            // Human creature token". Separate sentence, no
                            // conditional gate: created under the target's
                            // controller even if the destroy half fizzled.
                            if (tokenController != null)
                            {
                                var spec = new TokenFactory.TokenSpec(
                                    Name: "Human",
                                    Power: TokenPower,
                                    Toughness: TokenToughness,
                                    Subtypes: new[] { CardSubtype.Human },
                                    Keywords: null,
                                    Colors: new[] { ManaColor.White });

                                TokenFactory.CreateOnBattlefield(
                                    spec, tokenController, zoneService);
                            }
                        }),
                };
            });
    }
}
