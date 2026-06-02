using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snow-Covered Plains (Ice Age / reprints).
///
/// Type line: Basic Snow Land — Plains
///
/// Snow-Covered Plains has two supertypes — Basic AND Snow (CR 205.4d) —
/// and the Plains land subtype. The {T}: Add {W} mana ability is intrinsic
/// to the Plains subtype and is wired by <see cref="NamedCardFactory"/>'s
/// <c>AttachBasicLandMana</c> helper (same mechanism as the non-snow Plains),
/// which fires whenever a Land has the Basic supertype.
///
/// The Snow supertype matters for cards that care about snow permanents or
/// snow mana (e.g. Skred, Dead of Winter, Rime Tender).
/// </summary>
[CardName("Snow-Covered Plains")]
public static class SnowCoveredPlainsFactory
{
    /// <summary>
    /// Construct a Snow-Covered Plains owned and controlled by
    /// <paramref name="owner"/>.
    ///
    /// The {T}: Add {W} mana ability is NOT added here — it is attached by
    /// <see cref="NamedCardFactory.Create"/> via <c>AttachBasicLandMana</c>
    /// after dispatch, which runs for any Land with the Basic supertype.
    /// Tests that need the mana ability should obtain the card via
    /// <c>NamedCardFactory.Create("Snow-Covered Plains", owner)</c>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            "Snow-Covered Plains",
            new[] { CardSupertype.Basic, CardSupertype.Snow },
            new[] { CardSubtype.Plains });

        land.SetOwner(owner);
        land.SetController(owner);

        return land;
    }
}
