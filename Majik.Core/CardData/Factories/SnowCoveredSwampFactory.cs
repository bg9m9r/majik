using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Snow-Covered Swamp (Ice Age / reprints).
///
/// Type line: Basic Snow Land — Swamp
///
/// Snow-Covered Swamp has two supertypes — Basic AND Snow (CR 205.4d) —
/// and the Swamp land subtype. The {T}: Add {B} mana ability is intrinsic
/// to the Swamp subtype and is wired by <see cref="NamedCardFactory"/>'s
/// <c>AttachBasicLandMana</c> helper (same mechanism as the non-snow Swamp),
/// which fires whenever a Land has the Basic supertype.
///
/// The Snow supertype matters for cards that care about snow permanents or
/// snow mana (e.g. Skred, Dead of Winter, Marit Lage's Slumber).
/// </summary>
[CardName("Snow-Covered Swamp")]
public static class SnowCoveredSwampFactory
{
    /// <summary>
    /// Construct a Snow-Covered Swamp owned and controlled by
    /// <paramref name="owner"/>.
    ///
    /// The {T}: Add {B} mana ability is NOT added here — it is attached by
    /// <see cref="NamedCardFactory.Create"/> via <c>AttachBasicLandMana</c>
    /// after dispatch, which runs for any Land with the Basic supertype.
    /// Tests that need the mana ability should obtain the card via
    /// <c>NamedCardFactory.Create("Snow-Covered Swamp", owner)</c>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            "Snow-Covered Swamp",
            new[] { CardSupertype.Basic, CardSupertype.Snow },
            new[] { CardSubtype.Swamp });

        land.SetOwner(owner);
        land.SetController(owner);

        return land;
    }
}
