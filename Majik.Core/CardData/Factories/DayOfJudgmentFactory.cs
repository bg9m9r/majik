using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Day of Judgment (Magic 2010, {2}{W}{W}).
///
/// Sorcery. Oracle text:
///   "Destroy all creatures."
///
/// ## Distinction from <see cref="WrathOfGodFactory"/>
/// Day of Judgment differs from Wrath of God / Damnation by the absence of
/// the "They can't be regenerated." rider — a creature with an active
/// regeneration shield (CR 701.15) at the time Day of Judgment resolves
/// is regenerated rather than destroyed. The factory therefore passes
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> (regen-honouring)
/// to <see cref="OracleSpellBinder.MoveToGraveyard"/>, where Wrath /
/// Damnation pass <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>.
///
/// Indestructible (CR 702.12) is honoured identically — both reasons stop
/// the destroy on an indestructible creature.
///
/// ## Implementation
///
/// Sorcery shape only on the dispatcher path; the resolve effect is built
/// on demand via <see cref="BuildResolveEffect"/>. The effect iterates
/// every player supplied by the caller-provided list (typically
/// <c>Game.Players</c>), snapshots each player's battlefield, and routes
/// every <see cref="Creature"/> to its owner's graveyard via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/>. Snapshotting up front
/// avoids "collection modified" on the in-place zone mutation.
/// </summary>
[CardName("Day of Judgment")]
public static class DayOfJudgmentFactory
{
    public const string CardName = "Day of Judgment";
    public const string PrintedManaCost = "{2}{W}{W}";

    /// <summary>
    /// Build a Day of Judgment sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// effect via <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Day of Judgment's resolve effect — destroy every
    /// <see cref="Creature"/> on every supplied player's battlefield.
    /// Each creature is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> — regeneration
    /// shields are honoured (CR 701.15) and indestructible creatures
    /// survive (CR 702.12b).
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
                        // Day of Judgment: plain "Destroy all creatures" —
                        // CR 701.7 destroy effect with NO "can't be
                        // regenerated" rider. Regen shields apply
                        // (ZoneMoveReason.Destroy honours them).
                        OracleSpellBinder.MoveToGraveyard(c, Majik.Core.Zones.ZoneMoveReason.Destroy);
                    }
                }
            }),
        };
    }
}
