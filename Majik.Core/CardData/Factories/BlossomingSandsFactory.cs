using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Blossoming Sands (Khans of Tarkir).
///
/// G/W "life-gain dual land" (the Refuge cycle). Oracle text (verified
/// against Scryfall):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {G} or {W}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/blossoming-sands.json</c>. Identical oracle
/// shape to its W/U cycle-mate <see cref="TranquilCoveFactory"/>; only the
/// produced colours differ. The two single-colour mana abilities produce
/// {G} and {W} (CR 605.1a) and the ETB keyword action is a flat
/// "you gain 1 life" (CR 119.3). Unconditional ETB-tapped (CR 614.1c) is
/// applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience — matches the Refuge / Temple
/// cycle posture).
/// </summary>
[CardName("Blossoming Sands")]
public static class BlossomingSandsFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("blossoming-sands");

    /// <summary>Construct Blossoming Sands owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
