using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Doomskar (Kaldheim, {3}{W}{W}).
///
/// Sorcery. Oracle text:
///   "Foretell {2}{W} (During your turn, you may pay {2} and exile this
///    card from your hand face down. Cast it on a later turn for its
///    foretell cost.)
///    Destroy all creatures."
///
/// ## Foretell (CR 702.143) — not yet implemented
///
/// This engine does not yet model the Foretell alternative cost (CR
/// 702.143). Foretell needs three primitives the cast pipeline doesn't
/// yet expose:
///
/// 1. An activated-from-hand alt-cost binding: pay {2}, exile this card
///    face-down with a "foretold" marker, sorcery-speed only on your
///    turn (CR 702.143b).
/// 2. A cast-from-exile pipeline that consumes the foretold marker and
///    bills the printed foretell cost rather than the printed mana cost
///    on a subsequent turn (CR 702.143c, sibling of Suspend's last-
///    counter cast).
/// 3. <c>MechanicPrimitiveRegistry</c> already covers the
///    <c>foretell</c> primitive (CR 702.143) for deferral-sweep
///    discovery — the registry entry is in place, just no factories
///    wire it yet.
///
/// Until those land, this factory ships <b>without the foretell alt
/// cost</b>: callers can only cast Doomskar for its printed
/// <c>{3}{W}{W}</c> mana cost. The resolve body is the same as the
/// foretold cast would produce ("Destroy all creatures"), so once
/// Foretell is wired the only thing to add is the alt-cost surface (the
/// resolve closure stays put).
///
/// (defer: foretell alternative cost — CR 702.143. Today the factory
/// only exposes the printed mana cost path; the foretold {2}{W} cast
/// path is not yet available because the cast pipeline lacks the
/// foretell exile-face-down primitive.)
///
/// ## Resolve body
///
/// Card shape at the dispatcher; the resolve body is built on demand
/// via <see cref="BuildResolveEffect"/>. For every supplied player,
/// snapshot the battlefield and route every <see cref="Creature"/> to
/// its owner's graveyard via
/// <see cref="OracleSpellBinder.MoveToGraveyard"/> with the default
/// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> — printed text has no "can't
/// be regenerated" rider, so active regeneration shields (CR 701.15)
/// are consumed normally, and indestructible (CR 702.12) gates as
/// usual at the binder.
///
/// Distinct from <see cref="WrathOfGodFactory"/> in one detail: Wrath /
/// Damnation use <see cref="Majik.Core.Zones.ZoneMoveReason.DestroyNoRegeneration"/>
/// to honour their "They can't be regenerated." rider; Doomskar's
/// printed text lacks that line, so regen shields apply. Snapshotting
/// up front avoids "collection modified" on the in-place zone mutation.
///
/// CR rule references: 109.5 (symmetric sweep), 117.5 (mana cost),
/// 514.2 (cleanup step), 701.7 (destroy), 701.15 (regeneration),
/// 702.12 (indestructible), 702.143 (foretell — not yet implemented).
/// </summary>
[CardName("Doomskar")]
public static class DoomskarFactory
{
    public const string CardName = "Doomskar";
    public const string PrintedManaCost = "{3}{W}{W}";

    /// <summary>Foretell cost (CR 702.143) — not yet implemented. Held
    /// as a constant for the future cast-pipeline binding.</summary>
    public const string ForetellPrintedCost = "{2}{W}";

    /// <summary>
    /// Build a Doomskar sorcery owned and controlled by
    /// <paramref name="owner"/>. Card shape only — wire the resolve
    /// body via <see cref="BuildResolveEffect"/>.
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
    /// Build Doomskar's resolve effect — destroy every
    /// <see cref="Creature"/> on every supplied player's battlefield.
    /// Each victim is routed to its owner's graveyard via
    /// <see cref="OracleSpellBinder.MoveToGraveyard"/> with the default
    /// <see cref="Majik.Core.Zones.ZoneMoveReason.Destroy"/> reason.
    /// Indestructible (CR 702.12) cancels the destroy; active
    /// regeneration shields (CR 701.15) are consumed normally — Doomskar
    /// has no "can't be regenerated" rider, unlike Wrath of God.
    /// </summary>
    /// <param name="allPlayers">All players whose battlefields should be
    /// swept. Typically <c>Game.Players</c>; pass
    /// <c>new[] { caster }</c> for a controller-only sweep (off-oracle).</param>
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
                        // CR 701.7 — destroy. Default Destroy reason;
                        // regen shields apply (no rider on Doomskar).
                        OracleSpellBinder.MoveToGraveyard(
                            c, Majik.Core.Zones.ZoneMoveReason.Destroy);
                    }
                }
            }),
        };
    }
}
