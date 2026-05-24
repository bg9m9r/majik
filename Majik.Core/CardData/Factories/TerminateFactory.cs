using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Terminate (Planeshift / various reprints, {B}{R}).
///
/// Instant. Oracle text:
///   "Destroy target creature. It can't be regenerated."
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {B}{R}, owner / controller.
/// - <b>Destroy target creature</b> — <see cref="BuildSpellDefinition"/>
///   builds a <see cref="SpellDefinition"/> with a single 1..1
///   "target creature" <see cref="TargetRequest"/>. On resolution the
///   targeted creature is destroyed via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/> (CR 701.7) iff it
///   is still on the battlefield and is a creature (CR 608.2b).
///
/// Indestructible (CR 702.12) and the "it can't be regenerated" rider
/// (CR 701.15) are handled at the destroy site:
/// <see cref="OracleSpellBinder.MoveToGraveyard(ICard, ZoneMoveReason)"/>
/// is invoked with
/// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>,
/// so indestructible cancels the destroy (CR 702.12b) and any active
/// regeneration shield on the target is bypassed rather than consumed.
/// </summary>
[CardName("Terminate")]
public static class TerminateFactory
{
    public const string CardName = "Terminate";
    public const string PrintedManaCost = "{B}{R}";

    /// <summary>CardDef DSL — card shape only. Resolve behaviour
    /// (destroy with the "can't be regenerated" rider) is built on demand
    /// via <see cref="BuildSpellDefinition"/>.</summary>
    public static CardDef Define() => CardDef.Instant(CardName, PrintedManaCost);

    public static Instant Create(Player owner) =>
        (Instant)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build the <see cref="SpellDefinition"/> used when Terminate is
    /// cast. Single 1..1 "target creature" request; on resolution the
    /// targeted creature is destroyed (CR 701.7) iff it is still on the
    /// battlefield and is a creature (CR 608.2b — illegal target → no-op).
    ///
    /// The "it can't be regenerated" rider is honoured via
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
    /// (see class xmldoc).
    /// </summary>
    /// <param name="resolver">Resolves the raw target token to a
    /// live engine object (chosen target → live game object).</param>
    public static SpellDefinition BuildSpellDefinition(
        Func<object, object> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        return new SpellDefinition(
            Modes: Array.Empty<string>(),
            HasVariableX: false,
            TargetRequests: new[]
            {
                new TargetRequest(
                    "target creature",
                    1, 1,
                    Array.Empty<object>(),
                    BotIntent.Removal),
            },
            EffectFactory: chosen =>
            {
                var raw = resolver(chosen.Targets[0][0]);
                return new IEffect[]
                {
                    new Effect(
                        $"{CardName}: destroy target creature",
                        () =>
                        {
                            if (raw is not Creature target) return;

                            // CR 608.2b — resolution-time legality check.
                            // Target must still be a creature on the battlefield.
                            if (target.Zone != ZoneType.Battlefield) return;

                            // CR 701.7 — Destroy. "It can't be regenerated"
                            // is honoured via DestroyNoRegeneration:
                            // indestructible (CR 702.12) still cancels the
                            // destroy, but any active regeneration shield
                            // (CR 701.15) is bypassed.
                            OracleSpellBinder.MoveToGraveyard(target, Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration);
                        }),
                };
            });
    }
}
