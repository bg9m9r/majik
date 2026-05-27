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
/// Named-card factory for Terror (Alpha / various reprints, {1}{B}).
///
/// Instant. Oracle text:
///   "Destroy target nonartifact, nonblack creature.
///    It can't be regenerated."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, owner / controller.
/// - <b>Destroy target nonartifact, nonblack creature</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target nonartifact nonblack creature"
///   <see cref="TargetRequest"/>. On resolution the chosen creature is
///   validated via:
///     (a) <see cref="Majik.Core.Cards.CardColors.GetColors"/> for the
///         nonblack filter (CR 105 — colour derived from mana cost pips;
///         cards with no {B} pip are nonblack), and
///     (b) <c>!target.HasType(CardType.Artifact)</c> for the nonartifact
///         filter — artifact creatures are illegal targets.
///   CR 608.2b — illegal target at resolution → effect does nothing.
///
/// Indestructible (CR 702.12b) still cancels the destroy.
/// The "it can't be regenerated" rider (CR 701.15c) is honoured via
/// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>,
/// bypassing any active regeneration shield on the target rather than
/// consuming it (same approach as <see cref="TerminateFactory"/>).
/// </summary>
[CardName("Terror")]
public static class TerrorFactory
{
    public const string CardName = "Terror";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour is built
    /// on demand via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target nonartifact, nonblack creature" spell
    /// definition used when Terror resolves.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield,
    /// is nonblack (CR 105 colour filter), and is not an artifact
    /// (CR 608.2b — illegal-target filter at resolution).
    /// When valid, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
    /// (CR 701.7 + CR 701.15c) so indestructible prevents the destroy
    /// (CR 702.12b) but regeneration shields are bypassed.
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
                    Description: "target nonartifact nonblack creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Gather every nonblack, nonartifact creature on any
                    // battlefield. Removal intent in the bot's ranker
                    // pushes the opponent's biggest qualifying threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => !CardColors.GetColors(c).Contains(ManaColor.Black))
                        .Where(c => !c.HasType(CardType.Artifact))
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
                        $"{CardName}: destroy target nonartifact nonblack creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 105 — nonblack filter (no {B} pip in mana cost).
                            if (CardColors.GetColors(target).Contains(ManaColor.Black)) return;

                            // Nonartifact filter — artifact creatures are illegal targets.
                            if (target.HasType(CardType.Artifact)) return;

                            // CR 701.7 + CR 701.15c — Destroy with "can't be regenerated".
                            // Indestructible (CR 702.12b) still cancels the destroy;
                            // any regeneration shield is bypassed (DestroyNoRegeneration).
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);
                        }),
                };
            });
    }
}
