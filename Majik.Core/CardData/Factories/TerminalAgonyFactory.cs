using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Spells;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Terminal Agony (Modern Horizons 3, {2}{B}{R}).
///
/// Sorcery. Oracle text (verified against Scryfall 2026-06-16):
///   "Destroy target creature."
///   "Madness {B}{R} (If you discard this card, discard it into exile.
///    When you do, cast it for its madness cost or put it into your
///    graveyard.)"
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {2}{B}{R}, owner / controller.
/// - <b>Destroy target creature</b> — <see cref="BuildDefinition"/> builds a
///   <see cref="SpellDefinition"/> whose single 1..1 target request is the
///   shared declarative <c>creature</c> filter
///   (<see cref="TargetFilters.ToTargetRequest(string?, string, BotIntent, bool)"/>):
///   any battlefield creature, any controller (CR 601.2c). On resolution the
///   chosen creature is re-checked against the same filter
///   (<see cref="TargetFilters.Matches"/> — CR 608.2b: a creature that has left
///   the battlefield since the spell went on the stack fizzles cleanly) and
///   destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
///   <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///   (CR 702.12) / regeneration (CR 701.15) shields are honoured at the destroy
///   site.
///
/// Direct sibling of <see cref="MurderousCompulsionFactory"/> minus the
/// printed "tapped" restriction (Terminal Agony destroys ANY creature).
///
/// ## Madness {B}{R} — intrinsic, NOT wired here
/// Madness (CR 702.35) is handled engine-wide: when Terminal Agony is
/// discarded, the central discard funnel (<c>Fx.DiscardCard</c>) consults
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> (which lists
/// "Terminal Agony" → {B}{R}) and routes the card to exile with the option to
/// cast it for its madness cost via the replacement bus
/// (<see cref="Majik.Core.Effects.MadnessReplacement"/>). No per-card factory
/// code is required for the madness line — only the destroy body above.
/// </summary>
[CardName("Terminal Agony")]
public static class TerminalAgonyFactory
{
    public const string CardName = "Terminal Agony";
    public const string PrintedManaCost = "{2}{B}{R}";
    private const string TargetFilter = "creature";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target creature) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target creature" <see cref="SpellDefinition"/>.
    ///
    /// The target request is the shared declarative <c>creature</c> filter, so
    /// the candidate gatherer offers every battlefield creature (any controller)
    /// and the resolution re-check (<see cref="TargetFilters.Matches"/>) enforces
    /// CR 608.2b — an off-battlefield / non-creature target fizzles cleanly. The
    /// destroy goes through
    /// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/> with
    /// <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible /
    /// regeneration shields are honoured.
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
                TargetFilters.ToTargetRequest(TargetFilter, "destroy", BotIntent.Removal),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature",
                        () =>
                        {
                            // CR 608.2b — resolution-time legality re-check via
                            // the SAME declarative filter the gatherer used.
                            if (!TargetFilters.Matches(TargetFilter, resolved)) return;

                            // CR 701.7 — Destroy. Indestructible (CR 702.12) and
                            // regeneration (CR 701.15) honoured via the
                            // Destroy-reason gate in MoveToGraveyard.
                            OracleSpellBinder.MoveToGraveyard(
                                (ICard)resolved,
                                ZoneMoveReason.Destroy);
                        }),
                };
            });
    }
}
