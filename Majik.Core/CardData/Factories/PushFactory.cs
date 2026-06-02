using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the PUSH half of the split card Push // Pull
/// (Strixhaven: School of Mages, {1}{W/B} // {4}{B/R}{B/R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-02):
///   "Destroy target tapped creature."
///
/// Sister half — <see cref="PullFactory"/> ({4}{B/R}{B/R}; reanimate up to two
/// creature cards from a single graveyard).
///
/// ## Split-card modelling (CR 712 / CR 709)
///
/// A split card is a single physical card with two halves; the caster picks
/// one half on cast and casts only that half (CR 712.4a). v1 models each
/// printed half as its own <c>[CardName]</c>-dispatched factory — the same
/// minimal posture the engine uses for Wear // Tear (<see cref="WearFactory"/>
/// / <see cref="TearFactory"/>):
/// <list type="bullet">
///   <item>Casting Push → <see cref="NamedCardFactory"/> resolves
///     <c>"Push"</c> → this factory → a <see cref="Sorcery"/> with the
///     destroy-tapped-creature effect.</item>
///   <item>Casting Pull → <see cref="NamedCardFactory"/> resolves
///     <c>"Pull"</c> → <see cref="PullFactory"/> → a <see cref="Sorcery"/>
///     with the multi-target reanimate effect.</item>
/// </list>
/// The combined seed row <c>"Push // Pull"</c> flips <c>IsImplemented</c> via
/// the front-face check in <see cref="EmbeddedCardRepository"/> because the
/// front half <c>"Push"</c> is in the <see cref="ImplementedCardNames"/>
/// registry; <see cref="PushPullFactory"/> also dispatches the combined name
/// directly. Each half carries an <see cref="MdfcState"/> face tracker
/// (front = "Push", back = "Pull") so callers can observe the other half's
/// printed name from either object — same informational role MdfcState plays
/// for the Wear // Tear split.
///
/// ## Implemented (v1)
/// - Sorcery identity at {1}{W/B} (hybrid white/black — both colours derived
///   from the hybrid pip per CR 202.2 / CR 709.4), built from the embedded
///   JSON def (<c>push.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource(string)"/> +
///   <see cref="CardDefinitionFactory"/>.
/// - <see cref="MdfcState"/> attached on the front half (Push).
/// - <b>Destroy target tapped creature</b> — single 1..1 "target tapped
///   creature" <see cref="TargetRequest"/>; the <c>CandidateGatherer</c> walks
///   every player's battlefield, yielding tapped creature permanents
///   (CR 302.6 — a creature can be tapped/untapped). On resolution it re-checks
///   the target is still a <see cref="Creature"/> on the Battlefield AND tapped
///   (CR 608.2b illegal-target gate), then destroys via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///   (CR 702.12) / regeneration (CR 701.15) shields are honoured.
///
/// ## Deferred (v1 gap — shared with Wear // Tear)
/// - <b>Fuse / split-cast</b>: Push // Pull is NOT a fuse card, but the engine
///   still has no general split-cast surface; each half is castable
///   independently via its own <c>[CardName]</c> factory.
/// </summary>
[CardName("Push")]
public static class PushFactory
{
    public const string CardName = "Push";
    public const string SisterName = "Pull";
    public const string Slug = "push";
    public const string PrintedManaCost = "{1}{W/B}";

    /// <summary>
    /// Build the Push half as a Sorcery from the embedded JSON def, with the
    /// <see cref="MdfcState"/> face tracker attached (front = Push).
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var def = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Sorcery)CardDefinitionFactory.Build(def, owner);

        // CR 712 — attach the split-card face tracker so the sister half's
        // printed name (Pull) is observable from the Push object. Starts on
        // the front half. Informational only, matching the Wear // Tear posture.
        card.MdfcState = new MdfcState(CardName, SisterName);
        return card;
    }

    /// <summary>
    /// Build the "destroy target tapped creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates the resolved target is still a
    /// <see cref="Creature"/> on the Battlefield AND tapped (CR 608.2b —
    /// illegal target at resolution → no-op); then destroys via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible /
    /// regeneration shields are honoured.
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
                    Description: "target tapped creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt: walk every battlefield, yield creatures that
                    // are tapped (CR 302.6).
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => c.IsTapped)
                        .Cast<object>()
                        .ToList()),
            },
            EffectFactory: p =>
            {
                if (p.Targets.Count == 0 || p.Targets[0].Count == 0)
                    return Array.Empty<IEffect>();

                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target tapped creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // Oracle constraint: target must still be TAPPED at
                            // resolution (CR 608.2b). If it untapped in the
                            // interim the spell does nothing.
                            if (!target.IsTapped) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
