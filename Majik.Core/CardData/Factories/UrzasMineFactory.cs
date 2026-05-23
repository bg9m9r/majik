using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Urza's Mine (Antiquities — Urza Tron cycle).
///
/// Land — Urza's Mine. Oracle text:
///   "{T}: Add {C}. If you control an Urza's Mine, an Urza's
///    Power-Plant, and an Urza's Tower, add {2} instead."
///
/// ## Implemented (v1)
/// - Single <see cref="ManaAbility"/> using the dynamic
///   <c>Func&lt;ManaCost&gt;</c> overload — the mana amount is computed
///   per activation from the controller's battlefield via
///   <see cref="TronLandHelper.ComputeManaAddition"/>. {C} when the
///   Tron set isn't assembled, {2} when it is.
///
/// ## Subtypes
/// - <see cref="CardSubtype.Urzas"/> + <see cref="CardSubtype.Mine"/>.
///   No supertypes (not legendary, not basic).
/// </summary>
public static class UrzasMineFactory
{
    /// <summary>
    /// Construct Urza's Mine owned and controlled by <paramref name="owner"/>.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = new Land(
            "Urza's Mine",
            supertypes: null,
            subtypes: new[] { CardSubtype.Urzas, CardSubtype.Mine });
        land.SetOwner(owner);
        land.SetController(owner);

        land.AddAbility(new ManaAbility(
            source: land,
            controller: owner,
            manaGenerator: () => TronLandHelper.ComputeManaAddition(land.Controller ?? owner)));

        return land;
    }
}
