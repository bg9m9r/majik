using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boil (Stronghold and many reprints, {2}{R}).
///
/// Instant. Oracle text:
///   "Destroy all Islands."
///
/// ## Implementation
///
/// Sorcery shape only on the dispatcher path; the resolve effect is built
/// on demand via <see cref="BuildResolveEffect"/> — the caller supplies
/// every player whose battlefield should be swept (typically
/// <c>Game.Players</c>, since Boil is symmetric and hits every Island
/// regardless of controller — CR 109.5 / CR 700.3 destroy semantics).
///
/// The effect iterates each supplied player's battlefield, snapshots the
/// <see cref="Permanent"/>s with <see cref="CardSubtype.Island"/> (CR 205.3i
/// — Basic supertype is irrelevant; "Island" the subtype is what matches),
/// and routes each to its owner's graveyard via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/>. Indestructible
/// (CR 702.12) still cancels the destroy; regeneration shields (CR 701.15)
/// are honoured — the printed oracle has no "can't be regenerated" rider,
/// unlike Wrath of God / Damnation.
///
/// Snapshotting up front avoids "collection modified" while the
/// underlying zone mutates in place. Same shape as
/// <see cref="WrathOfGodFactory.BuildResolveEffect"/>; the only delta is
/// the per-permanent filter — <see cref="CardSubtype.Island"/> instead of
/// <see cref="CardType.Creature"/> — and the destroy reason (plain
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> rather than the
/// no-regen variant).
///
/// Boil is symmetric — it destroys Islands controlled by every player,
/// the caster included. No controller filter is applied.
/// </summary>
[CardName("Boil")]
public static class BoilFactory
{
    public const string CardName = "Boil";
    public const string PrintedManaCost = "{2}{R}";

    /// <summary>
    /// Build Boil with correct identity owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// effect via <see cref="BuildResolveEffect"/>.
    /// </summary>
    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Boil's resolve effect — destroy every permanent with
    /// <see cref="CardSubtype.Island"/> on every supplied player's
    /// battlefield. Each Island is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> — indestructible
    /// cancels the destroy and regeneration is honoured.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// scanned. Typically <c>Game.Players</c>; pass <c>new[] { caster }</c>
    /// for a controller-only sweep (off-oracle).</param>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        IReadOnlyList<Player> allPlayers)
    {
        ArgumentNullException.ThrowIfNull(allPlayers);

        return new IEffect[]
        {
            new Effect($"{CardName}: destroy all Islands.", () =>
            {
                // Snapshot every battlefield up front — MoveToGraveyard
                // mutates the source zone in place.
                foreach (var pl in allPlayers)
                {
                    var islands = pl.Zones.Battlefield.GetCards()
                        .OfType<Permanent>()
                        .Where(p => p.HasSubtype(CardSubtype.Island))
                        .ToList();
                    foreach (var island in islands)
                    {
                        // Plain Destroy — CR 701.7. Indestructible
                        // (CR 702.12b) cancels; regeneration shield
                        // (CR 701.15c) is consumed in place of the
                        // destroy. No "can't be regenerated" rider on
                        // Boil's printed oracle.
                        OracleSpellBinder.MoveToGraveyard(
                            island,
                            Majik.Core.Zones.ZoneMoveReason.Destroy);
                    }
                }
            }),
        };
    }
}
