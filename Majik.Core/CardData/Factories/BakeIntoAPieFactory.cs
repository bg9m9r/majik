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
/// Named-card factory for Bake into a Pie (Throne of Eldraine, {2}{B}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-14):
///   "Destroy target creature. Create a Food token. (It's an artifact with
///    "{2}, {T}, Sacrifice this token: You gain 3 life.")"
///
/// Bake into a Pie is the destroy-a-creature cousin of
/// <see cref="BedevilFactory"/> / <see cref="HerosDownfallFactory"/> — the
/// same instant-speed Destroy resolve, narrowed to <b>creature only</b>, plus
/// a Food-token mint borrowed from <see cref="WitchsOvenFactory"/> /
/// <see cref="TokenFactory.CreateFood"/>.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {2}{B}{B}. The base shape (name /
///   Instant type / {2}{B}{B} cost) is materialised from the embedded JSON
///   definition (<c>bake-into-a-pie.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same posture as
///   <see cref="BedevilFactory"/> (the JSON <c>SpellDefinition</c> schema does
///   not yet express a destroy-then-mint-token resolve, so the resolve
///   behaviour is layered on here via <see cref="BuildDefinition"/>).
/// - <b>Destroy target creature</b> — <see cref="BuildDefinition"/> returns a
///   <see cref="SpellDefinition"/> with a single 1..1 "target creature"
///   <see cref="TargetRequest"/>. The live <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding cards with
///   <see cref="CardType.Creature"/>. The bot's
///   <see cref="BotIntent.Removal"/> ranker pushes opponent permanents up.
/// - On resolution: re-checks the target is still a Creature on the
///   Battlefield (CR 608.2b illegal-target gate), then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
///   indestructible (CR 702.12) / regeneration (CR 701.15) shields are
///   honoured.
/// - <b>Create a Food token</b> — unconditionally mints one Food token
///   (CR 111.10) for the caster via <see cref="TokenFactory.CreateFood"/>.
///   The Food half is NOT gated on the destroy half succeeding: the printed
///   wording is two independent sentences, so the token is created even if the
///   destroy fizzles to an illegal target.
/// </summary>
[CardName("Bake into a Pie")]
public static class BakeIntoAPieFactory
{
    public const string CardName = "Bake into a Pie";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "bake-into-a-pie";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {2}{B}{B}) from
    /// the embedded JSON definition. Resolve behaviour (destroy target
    /// creature + create a Food token) is built on demand via
    /// <see cref="BuildDefinition"/>, mirroring <see cref="BedevilFactory"/>.
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
    /// Build the "destroy target creature; create a Food token"
    /// <see cref="SpellDefinition"/>. On resolve: validates the target is
    /// still a Creature on the Battlefield (CR 608.2b — illegal target →
    /// destroy half is a no-op); when valid, destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
    /// indestructible / regeneration shields are honoured. Then mints one
    /// Food token for the caster (CR 111.10) — unconditionally, since the
    /// printed wording is two independent sentences.
    /// </summary>
    /// <param name="caster">Controller of Bake into a Pie — also the player
    /// who receives the Food token on resolve (CR 111.10 — a token is created
    /// under the control of the player the spell instructs).</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// permanents directly.</param>
    /// <param name="zoneService">Optional ZoneService so the minted Food
    /// token's battlefield ETB publishes <c>CardMovedEvent</c>; null in shape
    /// tests.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver,
        ZoneService? zoneService = null)
    {
        ArgumentNullException.ThrowIfNull(caster);
        ArgumentNullException.ThrowIfNull(targetResolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    Description: "target creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every creature on any
                    // battlefield. Removal intent in the bot's ranker pushes
                    // opponent permanents up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .Where(c => c.HasType(CardType.Creature))
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
                        $"{CardName}: destroy target creature; create a Food token",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check for
                            // the destroy half only. An illegal target does not
                            // stop the second sentence (the Food mint).
                            if (resolved is Permanent target
                                && target.Zone == ZoneType.Battlefield
                                && target.HasType(CardType.Creature))
                            {
                                // CR 701.7 — Destroy. Indestructible
                                // (CR 702.12) / regeneration (CR 701.15)
                                // honoured via the Destroy-reason gate.
                                OracleSpellBinder.MoveToGraveyard(
                                    target,
                                    Majik.Core.Zones.ZoneMoveReason.Destroy);
                            }

                            // CR 111.10 — create one Food token for the caster.
                            // Unconditional: the printed second sentence is not
                            // gated on the destroy resolving.
                            TokenFactory.CreateFood(caster, zoneService);
                        }),
                };
            });
    }
}
