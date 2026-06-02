using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Costs;

/// <summary>
/// CR 118.9 — "sacrifice two lands of a subtype" alternative cost.
///
///   "You may sacrifice two Mountains rather than pay this spell's mana cost."
///
/// Oracle pattern used by Fireblast (Visions/Tempest) and Snuff Out's
/// cousins. The caster chooses two lands they control of the required
/// subtype (Mountain for Fireblast) and sacrifices them instead of paying
/// the spell's printed mana cost.
///
/// Mirrors <see cref="SacrificeNontokenBlueCreatureAlternativeCost"/> (the
/// "sacrifice a permanent rather than pay" shape), generalized to:
///   * a required <see cref="CardSubtype"/> predicate (here a basic-land
///     subtype, CR 305.6), and
///   * exactly two distinct permanents (CR 701.18 sacrifice; the spell
///     specifies a fixed count of two).
///
/// Like Daze / Flare of Denial it carries no printed timing restriction —
/// available whenever the spell is otherwise castable. No mana is paid
/// (<see cref="AlternativeManaCost"/> = <see cref="ManaCost.Zero"/>); the
/// two sacrifices are the entire cost.
/// </summary>
public sealed class SacrificeTwoLandsAlternativeCost : IAlternativeCost
{
    /// <summary>The required land subtype each sacrificed permanent must
    /// carry (e.g. <see cref="CardSubtype.Mountain"/> for Fireblast).</summary>
    public CardSubtype RequiredSubtype { get; }

    /// <summary>The two lands the caster chose to sacrifice.</summary>
    public IReadOnlyList<ICard> SacrificedLands { get; }

    /// <summary>The fixed number of lands this alt cost sacrifices (two).</summary>
    public const int RequiredCount = 2;

    public SacrificeTwoLandsAlternativeCost(CardSubtype requiredSubtype, IReadOnlyList<ICard> sacrificedLands)
    {
        RequiredSubtype = requiredSubtype;
        SacrificedLands = sacrificedLands ?? throw new ArgumentNullException(nameof(sacrificedLands));
    }

    /// <inheritdoc/>
    public string Description =>
        $"Sacrifice two {RequiredSubtype}s instead of paying mana cost";

    /// <inheritdoc/>
    /// <remarks>No mana is paid — CR 118.9. The two sacrifices are the cost.</remarks>
    public ManaCost AlternativeManaCost => ManaCost.Zero;

    /// <summary>
    /// CR 118.9 legality. Exactly two distinct permanents must be supplied;
    /// each must be on the battlefield, controlled by the caster
    /// (CR 701.18a), carry the required land subtype, and not be the spell
    /// being cast.
    /// </summary>
    public bool CanCastFor(ICard card, Player caster)
    {
        if (caster == null) return false;
        if (SacrificedLands.Count != RequiredCount) return false;

        // Two distinct permanents — a single Mountain can't be sacrificed twice.
        if (ReferenceEquals(SacrificedLands[0], SacrificedLands[1])) return false;

        foreach (var land in SacrificedLands)
        {
            if (land == null) return false;
            if (ReferenceEquals(land, card)) return false;
            if (land.Zone != ZoneType.Battlefield) return false;
            if (!ReferenceEquals(land.Controller, caster)) return false;
            if (!land.HasType(CardType.Land)) return false;
            if (!land.HasSubtype(RequiredSubtype)) return false;
        }
        return true;
    }

    /// <summary>
    /// Apply the sacrifice after the spell resolves: move both chosen lands
    /// Battlefield → Graveyard (CR 701.18). Idempotent per land — safe if a
    /// land has already left the battlefield.
    /// </summary>
    public void OnResolved(ICard card, Player caster)
    {
        foreach (var land in SacrificedLands)
        {
            if (land.Zone != ZoneType.Battlefield) continue;
            caster.Zones.Battlefield.RemoveCard(land);
            caster.Zones.Graveyard.AddCard(land);
            land.SetZone(ZoneType.Graveyard);
        }
    }
}
