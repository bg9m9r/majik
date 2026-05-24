using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Urza's Power-Plant (Antiquities — Urza Tron
/// cycle).
///
/// Land — Urza's Power-Plant. Oracle text:
///   "{T}: Add {C}. If you control an Urza's Mine, an Urza's
///    Power-Plant, and an Urza's Tower, add {2} instead."
///
/// Same shape as <see cref="UrzasMineFactory"/>; only the printed
/// subtype differs (<see cref="CardSubtype.PowerPlant"/>). The shared
/// conditional mana logic lives in <see cref="TronLandHelper"/>.
/// </summary>
[CardName("Urza's Power-Plant")]
public static class UrzasPowerPlantFactory
{
    /// <summary>
    /// Construct Urza's Power-Plant owned and controlled by
    /// <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            "Urza's Power-Plant",
            supertypes: null,
            subtypes: new[] { CardSubtype.Urzas, CardSubtype.PowerPlant });
        land.SetOwner(owner);
        land.SetController(owner);

        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerator: () => TronLandHelper.ComputeManaAddition(land.Controller ?? owner)));

        return land;
    }
}
