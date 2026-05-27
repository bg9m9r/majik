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
/// Named-card factory for Vendetta (Mirage / various reprints, {B}).
///
/// Instant. Oracle text:
///   "Destroy target nonblack creature. It can't be regenerated.
///    You lose life equal to that creature's toughness."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}, owner / controller.
/// - <b>Destroy target nonblack creature; it can't be regenerated</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target nonblack creature"
///   <see cref="TargetRequest"/>. On resolution the chosen creature is
///   validated via <see cref="Majik.Core.Cards.CardColors.GetColors"/> for
///   the nonblack filter (CR 105 — colour derived from mana cost pips; cards
///   with no {B} pip are nonblack) and is still a Creature on the Battlefield
///   (CR 608.2b — illegal target at resolution → the entire spell does
///   nothing, including the life-loss rider).
///
///   Destruction uses <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
///   (CR 701.7 + CR 701.15c) — indestructible (CR 702.12b) still cancels the
///   destroy; regeneration shields are bypassed, not consumed.
///
/// - <b>You lose life equal to that creature's toughness</b> (CR 119.3) —
///   the target's <see cref="Majik.Core.Cards.Creature.Toughness"/> is
///   captured <em>before</em> destruction so the value is read while the
///   creature is still on the battlefield. Applied to the caster as part of
///   the same resolution effect, gated on the same legality check so it is
///   skipped when the target is illegal (CR 608.2b parity with Ulcerate /
///   Thoughtseize).
/// </summary>
[CardName("Vendetta")]
public static class VendettaFactory
{
    public const string CardName = "Vendetta";
    public const string PrintedManaCost = "{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour is built
    /// on demand via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target nonblack creature (can't regenerate); you
    /// lose life equal to its toughness" <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// AND is nonblack (CR 608.2b — illegal-target filter at resolution).
    /// When valid:
    ///   1. Captures the target's toughness before destroying it.
    ///   2. Destroys the target via
    ///      <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    ///      <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
    ///      (CR 701.7 + CR 701.15c).
    ///   3. Applies life loss to the caster equal to the captured toughness
    ///      (CR 119.3).
    /// </summary>
    /// <param name="caster">Cast-time controller — suffers the life-loss
    /// equal to the target's toughness.</param>
    /// <param name="targetResolver">Maps the agent-supplied raw target token
    /// to the live engine object. Pass <c>o =&gt; o</c> for tests that hand
    /// creatures directly.</param>
    public static SpellDefinition BuildDefinition(
        Player caster,
        Func<object, object> targetResolver)
    {
        ArgumentNullException.ThrowIfNull(caster);
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
                    // Gather every nonblack creature on any battlefield.
                    // Removal intent in the bot's ranker pushes the
                    // opponent's biggest nonblack threat up.
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
                        $"{CardName}: destroy target nonblack creature (can't regenerate); you lose life equal to its toughness",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            // If the target is illegal the entire spell does
                            // nothing — including the life-loss rider.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 105 — nonblack filter (no {B} pip in mana cost).
                            if (CardColors.GetColors(target).Contains(ManaColor.Black)) return;

                            // Capture toughness BEFORE destruction (the
                            // creature may leave the battlefield / have its
                            // base stats reset after the move).
                            var toughness = target.Toughness;

                            // CR 701.7 + CR 701.15c — Destroy; can't regenerate.
                            // Indestructible (CR 702.12b) still cancels;
                            // regeneration shields are bypassed via
                            // DestroyNoRegeneration rather than consumed.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);

                            // CR 119.3 — "You lose life equal to that
                            // creature's toughness." Applied to the caster.
                            caster.LoseLife(toughness);
                        }),
                };
            });
    }
}
