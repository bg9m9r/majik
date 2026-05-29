using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snow-Covered Island (Ice Age / reprints).
///
/// Type line: Basic Snow Land — Island
///
/// Snow-Covered Island has two supertypes — Basic AND Snow (CR 205.4d) —
/// and the Island land subtype. The {T}: Add {U} mana ability is intrinsic
/// to the Island subtype and is wired by <see cref="NamedCardFactory"/>'s
/// <c>AttachBasicLandMana</c> helper (same mechanism as the non-snow Island),
/// which fires whenever a Land has the Basic supertype.
///
/// The Snow supertype matters for cards that care about snow permanents or
/// snow mana (e.g. Skred, Dead of Winter, Rime Tender).
/// </summary>
[CardName("Snow-Covered Island")]
public static class SnowCoveredIslandFactory
{
    /// <summary>
    /// Construct a Snow-Covered Island owned and controlled by
    /// <paramref name="owner"/>.
    ///
    /// The {T}: Add {U} mana ability is NOT added here — it is attached by
    /// <see cref="NamedCardFactory.Create"/> via <c>AttachBasicLandMana</c>
    /// after dispatch, which runs for any Land with the Basic supertype.
    /// Tests that need the mana ability should obtain the card via
    /// <c>NamedCardFactory.Create("Snow-Covered Island", owner)</c>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            "Snow-Covered Island",
            new[] { CardSupertype.Basic, CardSupertype.Snow },
            new[] { CardSubtype.Island });

        land.SetOwner(owner);
        land.SetController(owner);

        return land;
    }
}
