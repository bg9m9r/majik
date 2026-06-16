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
/// Named-card factory for Murderous Compulsion (Innistrad, {1}{B}).
///
/// Sorcery. Oracle text:
///   "Destroy target tapped creature."
///   "Madness {1}{B}"
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{B}, owner / controller.
/// - <b>Destroy target tapped creature</b> — <see cref="BuildDefinition"/>
///   builds a <see cref="SpellDefinition"/> whose single 1..1 target request
///   is the shared declarative <c>tapped_creature</c> filter
///   (<see cref="TargetFilters.ToTargetRequest(string?, string, BotIntent, bool)"/>):
///   ANY tapped battlefield creature (CR 109.5), regardless of controller. On
///   resolution the chosen creature is re-checked against the same filter
///   (<see cref="TargetFilters.Matches"/> — CR 608.2b: a creature that has
///   untapped since the spell went on the stack fizzles cleanly) and destroyed
///   via <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
///   with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible
///   (CR 702.12) / regeneration (CR 701.15) shields are honoured.
///
/// ## Madness (CR 702.35)
/// Madness {1}{B} is engine-intrinsic: the cost is catalogued in
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/> and the discard-to-exile /
/// cast-for-madness funnel runs through the replacement bus
/// (<see cref="Majik.Core.Effects.MadnessReplacement"/>). This factory only
/// supplies the card BODY — no madness wiring is needed here.
/// </summary>
[CardName("Murderous Compulsion")]
public static class MurderousCompulsionFactory
{
    public const string CardName = "Murderous Compulsion";
    public const string PrintedManaCost = "{1}{B}";
    private const string TargetFilter = "tapped_creature";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour (destroy
    /// target tapped creature) is built on demand via
    /// <see cref="BuildDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Sorcery(CardName, PrintedManaCost);

    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the "destroy target tapped creature" <see cref="SpellDefinition"/>.
    ///
    /// The target request is the shared declarative <c>tapped_creature</c>
    /// filter, so the candidate gatherer offers exactly the tapped battlefield
    /// creatures (any controller) and the resolution re-check
    /// (<see cref="TargetFilters.Matches"/>) enforces CR 608.2b — an untapped /
    /// off-battlefield / non-creature target fizzles cleanly. The destroy goes
    /// through <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
    /// with <see cref="ZoneMoveReason.Destroy"/> (CR 701.7) so indestructible /
    /// regeneration shields are honoured at the destroy site.
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
                TargetFilters.ToTargetRequest(TargetFilter, "destroy", BotIntent.Removal),
            },
            EffectFactory: p =>
            {
                var raw = p.Targets[0][0];
                var resolved = targetResolver(raw);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target tapped creature",
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
