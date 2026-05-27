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
/// Named-card factory for Smother (Onslaught / various reprints, {1}{B}).
///
/// Instant. Oracle text:
///   "Destroy target creature with mana value 3 or less.
///    It can't be regenerated."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{B}, owner / controller.
/// - <b>Destroy target creature with mana value 3 or less</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target creature with mana value 3 or less"
///   <see cref="TargetRequest"/>. On resolution the chosen creature is
///   validated via:
///     (a) Still a Creature on the Battlefield (CR 608.2b — illegal-target
///         filter at resolution → no-op if it has left), and
///     (b) <c>ManaCost.Parse(target.ManaCost).TotalValue &lt;= ManaValueCap</c>
///         (CR 202.3 — mana value equals the total cost of all mana symbols in
///         the mana cost; CR 608.2b — creature above the cap → no-op).
///
/// The "it can't be regenerated" rider (CR 701.15c) is honoured via
/// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>,
/// bypassing any active regeneration shield rather than consuming it —
/// same approach as <see cref="TerrorFactory"/> / <see cref="TerminateFactory"/>.
/// Indestructible (CR 702.12b) still cancels the destroy.
/// </summary>
[CardName("Smother")]
public static class SmotherFactory
{
    public const string CardName = "Smother";
    public const string PrintedManaCost = "{1}{B}";

    /// <summary>Printed mana-value cap on the legal target (CR 202.3).</summary>
    public const int ManaValueCap = 3;

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (destroy target creature with mana value ≤ 3, can't be regenerated)
    /// is built on demand via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target creature with mana value 3 or less"
    /// <see cref="SpellDefinition"/> used when Smother resolves.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// and has a mana value ≤ 3 (CR 608.2b — illegal-target check at
    /// resolution). When valid, destroys the target via
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
                    Description: "target creature with mana value 3 or less",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Gather every creature on any battlefield whose mana value
                    // is ≤ 3 (CR 202.3). Removal intent in the bot's ranker
                    // pushes the opponent's biggest qualifying threat up.
                    CandidateGatherer: ctx => ctx.AllPlayers
                        .SelectMany(p => p.Zones.Battlefield.GetCards())
                        .OfType<Creature>()
                        .Where(c => ManaCost.Parse(c.ManaCost).TotalValue <= ManaValueCap)
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
                        $"{CardName}: destroy target creature with mana value 3 or less",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 202.3 — mana value filter: creature above the
                            // cap is an illegal target at resolution → no-op.
                            if (ManaCost.Parse(target.ManaCost).TotalValue > ManaValueCap) return;

                            // CR 701.7 + CR 701.15c — Destroy with "can't be
                            // regenerated". Indestructible (CR 702.12b) still
                            // cancels the destroy; regeneration shields are
                            // bypassed (DestroyNoRegeneration).
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);
                        }),
                };
            });
    }
}
