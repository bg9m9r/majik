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
/// Named-card factory for Dark Banishing (Fallen Empires, {2}{B}).
///
/// Instant. Oracle text:
///   "Destroy target nonblack creature. It can't be regenerated."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {2}{B}, owner / controller.
/// - <b>Destroy target nonblack creature, can't be regenerated</b> —
///   <see cref="BuildDefinition"/> builds a <see cref="SpellDefinition"/>
///   with a single 1..1 "target nonblack creature"
///   <see cref="TargetRequest"/>. On resolution the chosen creature is
///   filtered via <see cref="Majik.Core.Cards.CardColors.GetColors"/>
///   (CR 105 — colour derived from mana cost pips; cards with no black pip
///   are nonblack) and destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>
///   (CR 701.7) with <see cref="ZoneMoveReason.DestroyNoRegeneration"/>
///   iff it is still a Creature on the Battlefield (CR 608.2b — illegal
///   target at resolution → no-op).
///
/// Unlike <see cref="DoomBladeFactory"/> (no nonartifact restriction),
/// Dark Banishing targets <em>any</em> nonblack creature.
/// The no-regeneration clause is honoured via
/// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
/// which bypasses regeneration shields (CR 701.15c).
/// Indestructible (CR 702.12) is still honoured by the destroy gate.
/// </summary>
[CardName("Dark Banishing")]
public static class DarkBanishingFactory
{
    public const string CardName = "Dark Banishing";
    public const string PrintedManaCost = "{2}{B}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target nonblack creature, can't be regenerated) is built on demand
    /// via <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target nonblack creature, can't be regenerated"
    /// <see cref="SpellDefinition"/>.
    ///
    /// On resolve: validates target is still a Creature on the Battlefield
    /// AND is nonblack (CR 608.2b — illegal-target filter at resolution).
    /// When valid, destroys the target via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
    /// (CR 701.7 + CR 701.15c) so regeneration shields are bypassed and
    /// the creature cannot regenerate in response.
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
                    Description: "target nonblack creature",
                    MinTargets: 1,
                    MaxTargets: 1,
                    LegalCandidates: Array.Empty<object>(),
                    Intent: BotIntent.Removal,
                    // Agent-prompt MVP: live gather every nonblack creature on
                    // any battlefield. No nonartifact restriction (unlike Terror).
                    // Removal intent in the bot's ranker pushes the opponent's
                    // biggest nonblack threat up.
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
                        $"{CardName}: destroy target nonblack creature, can't be regenerated",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check.
                            if (resolved is not Creature target) return;
                            if (target.Zone != ZoneType.Battlefield) return;
                            // CR 105 — nonblack filter (no {B} pip in mana cost).
                            if (CardColors.GetColors(target).Contains(ManaColor.Black)) return;

                            // CR 701.7 — Destroy. CR 701.15c — can't be regenerated.
                            // DestroyNoRegeneration bypasses regeneration shields so
                            // the creature cannot regenerate from this effect.
                            // Indestructible (CR 702.12) is still honoured by the
                            // destroy gate.
                            OracleSpellBinder.MoveToGraveyard(
                                target,
                                Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);
                        }),
                };
            });
    }
}
