using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dark Withering (Torment, {4}{B}{B}).
///
/// Instant. Oracle text (verified against Scryfall 2026-06-10):
///   "Destroy target nonblack creature.
///    Madness {B}"
///
/// ## Madness is intrinsic — NOT wired here
/// CR 702.35 Madness is handled engine-wide via
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> (name → cost,
/// "Dark Withering" = {B}) consulted by the central discard funnel
/// <c>Fx.DiscardCard</c>. The "Madness {B}" oracle line therefore needs no
/// factory code; this factory implements only the spell body.
///
/// ## Implemented (v1)
/// - <b>Instant shape</b> at printed cost {4}{B}{B}, materialised from the
///   embedded JSON definition (<c>dark-withering.json</c>) via
///   <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
///   <see cref="CardDefinitionFactory.Build"/> — same data-only posture as
///   <see cref="FeedTheSwarmFactory"/> (the JSON <c>SpellDefinition</c> schema
///   does not express the nonblack target filter, so the resolve behaviour is
///   layered on here via <see cref="BuildDefinition"/>).
/// - <b>Destroy target nonblack creature</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single 1..1 "target nonblack
///   creature" <see cref="TargetRequest"/>. On resolution the chosen creature is
///   filtered via <see cref="Majik.Core.Cards.CardColors.GetColors"/> (CR 105 —
///   colour derived from mana cost pips; cards with no black pip are nonblack)
///   and destroyed via <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) iff it is still a Creature on the Battlefield (CR 608.2b —
///   illegal target at resolution → no-op). Identical body to
///   <see cref="DoomBladeFactory"/>.
///
/// Indestructible (CR 702.12) and regeneration shields (CR 701.15) are honoured
/// by <see cref="OracleSpellBinder.MoveToGraveyard"/>'s
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason gate — Dark
/// Withering does NOT print "can't be regenerated".
/// </summary>
[CardName("Dark Withering")]
public static class DarkWitheringFactory
{
    public const string CardName = "Dark Withering";

    /// <summary>JSON slug for the embedded base-shape definition.</summary>
    public const string Slug = "dark-withering";

    /// <summary>
    /// Materialise the Instant card shape (name / Instant / {4}{B}{B}) from the
    /// embedded JSON definition. Resolve behaviour (destroy target nonblack
    /// creature) is built on demand via <see cref="BuildDefinition"/>, mirroring
    /// <see cref="FeedTheSwarmFactory"/>.
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
    /// Build the "destroy target nonblack creature" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield AND
    /// is nonblack (CR 608.2b — illegal-target filter at resolution). When valid,
    /// destroys the target via <see cref="OracleSpellBinder.MoveToGraveyard"/>
    /// with <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (CR 701.7) so
    /// indestructible / regeneration shields are honoured at the destroy site.
    /// </summary>
    /// <param name="targetResolver">Maps the agent-supplied raw target token to
    /// the live engine object. Pass <c>o =&gt; o</c> for tests that hand
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
                    Description: "target nonblack creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every nonblack creature on
                    // any battlefield. Removal intent in the bot's ranker
                    // pushes the opponent's biggest nonblack threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !CardColors.GetColors(c).Contains(ManaColor.Black))
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
                        $"{CardName}: destroy target nonblack creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 105 — nonblack filter (no {B} pip in mana cost).
                            if (CardColors.GetColors(target).Contains(ManaColor.Black)) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12)
                            // and regeneration (CR 701.15) handled via the
                            // Destroy-reason gate in MoveToGraveyard (Dark
                            // Withering does NOT print "can't be regenerated").
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
