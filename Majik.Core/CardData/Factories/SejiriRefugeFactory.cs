using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sejiri Refuge (Zendikar) — a W/U member of the
/// "Refuge" life-gain tapland cycle.
///
/// Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {W} or {U}."
///
/// Same oracle shape as the Theros scry-land (<see cref="TempleOfTriumphFactory"/>)
/// and the Murders at Karlov Manor surveil-land cycle
/// (<see cref="CommercialDistrictFactory"/>) — only the ETB keyword action
/// differs (here: gain 1 life, CR 119.3, via the data-driven
/// <c>gain_life_self</c> effect). Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/sejiri-refuge.json</c> and materializes it
/// through <see cref="CardDefinitionFactory"/>.
///
/// Unconditional enters-tapped (CR 614.1c) is applied on the production
/// load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this
/// factory builds the land without it, for test convenience) — same posture
/// as the scry-land / surveil-land factories.
/// </summary>
[CardName("Sejiri Refuge")]
public static class SejiriRefugeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sejiri-refuge");

    /// <summary>Construct Sejiri Refuge owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
