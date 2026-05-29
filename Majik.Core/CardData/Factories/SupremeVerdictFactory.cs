using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Supreme Verdict (Return to Ravnica, {1}{W}{W}{U}).
///
/// Sorcery. Oracle text:
///   "This spell can't be countered.
///    Destroy all creatures."
///
/// ## Implemented (v1)
/// - Sorcery shape, mana cost {1}{W}{W}{U} (Azorius — white + blue),
///   owner / controller. Built via <see cref="CardDef"/> so the
///   structural keyword marker rides on the card shape, mirroring
///   <see cref="AbruptDecayFactory"/> / <see cref="DovinsVetoFactory"/>.
/// - <b>Can't be countered</b> — a <see cref="KeywordAbility"/> marker
///   "Can't Be Countered" is attached to the card shape (structural
///   observability, same pattern as <see cref="AbruptDecayFactory"/>).
///   Enforcement at the SpellCaster / StackResolver layer is deferred —
///   same posture as Abrupt Decay's marker (CR 701.5b). See
///   <see cref="CantBeCounteredMarker"/>.
/// - <b>Destroy all creatures</b> — the resolve effect is built on demand
///   via <see cref="BuildResolveEffect"/>. It iterates every player
///   supplied by the caller-provided list (typically <c>Game.Players</c>),
///   snapshots each player's battlefield, and routes every
///   <see cref="Creature"/> to its owner's graveyard via
///   <see cref="OracleSpellBinder.MoveToGraveyard"/>. Snapshotting up
///   front avoids "collection modified" on the in-place zone mutation.
///
/// ## Distinction from <see cref="WrathOfGodFactory"/>
/// Supreme Verdict, like <see cref="DayOfJudgmentFactory"/>, reads a plain
/// "Destroy all creatures" with NO "they can't be regenerated" rider — a
/// creature with an active regeneration shield (CR 701.15) at resolution
/// is regenerated rather than destroyed. The sweep therefore passes
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (regen-honouring),
/// where Wrath of God / Damnation pass
/// <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>.
/// Indestructible (CR 702.12b) is honoured identically by both reasons.
///
/// ## Deferred (v1 gaps)
/// - <b>Can't-be-countered enforcement</b>: the keyword marker is attached
///   but counter effects (Negate, Force of Negation, …) do not yet consult
///   it at the StackResolver / SpellCaster layer. Same deferral as
///   <see cref="AbruptDecayFactory"/>.
/// </summary>
[CardName("Supreme Verdict")]
public static class SupremeVerdictFactory
{
    public const string CardName = "Supreme Verdict";
    public const string PrintedManaCost = "{1}{W}{W}{U}";

    /// <summary>
    /// Keyword name used for the "this spell can't be countered" marker.
    /// Attached to the card shape as a <see cref="KeywordAbility"/> for
    /// structural observability (identical convention to
    /// <see cref="AbruptDecayFactory.CantBeCounteredMarker"/>).
    /// </summary>
    public const string CantBeCounteredMarker = "Can't Be Countered";

    /// <summary>CardDef DSL — card shape + "Can't Be Countered" marker.
    /// Resolve behaviour is built via <see cref="BuildResolveEffect"/>.</summary>
    public static CardDef Define() => CardDef
        .Sorcery(CardName, PrintedManaCost)
        .WithKeyword(CantBeCounteredMarker);

    /// <summary>
    /// Build a Supreme Verdict sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape + can't-be-countered marker
    /// only — wire the resolve effect via <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner) =>
        (Sorcery)CardDefRuntime.Build(Define(), owner);

    /// <summary>
    /// Build Supreme Verdict's resolve effect — destroy every
    /// <see cref="Creature"/> on every supplied player's battlefield.
    /// Each creature is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> — regeneration
    /// shields are honoured (CR 701.15) and indestructible creatures
    /// survive (CR 702.12b). Matches <see cref="DayOfJudgmentFactory"/>;
    /// Supreme Verdict's only printed rider is "can't be countered", not
    /// "can't be regenerated".
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep.</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"{CardName}: destroy all creatures.", () =>
            {
                // Snapshot every battlefield up front — MoveToGraveyard
                // mutates the source zone in place.
                foreach (var pl in allPlayers)
                {
                    var creatures = pl.Zones.Battlefield.GetCards()
                        .OfType<Creature>()
                        .ToList();
                    foreach (var c in creatures)
                    {
                        // CR 701.7 destroy with NO "can't be regenerated"
                        // rider — regen shields apply (ZoneMoveReason.Destroy
                        // honours them); indestructible still survives.
                        OracleSpellBinder.MoveToGraveyard(c, Majik.Core.Zones.ZoneMoveReason.Destroy);
                    }
                }
            }),
        };
    }
}
