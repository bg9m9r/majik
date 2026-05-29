using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Jwar Isle Refuge (Worldwake) — a member of the
/// Zendikar/Worldwake "Refuge" gain-life dual-land cycle.
///
/// U/B "Refuge" land. Oracle text:
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {U} or {B}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/jwar-isle-refuge.json</c>. Same oracle shape
/// as the Theros scry-land cycle (<see cref="TempleOfTriumphFactory"/>) and
/// the Murders at Karlov Manor surveil-land cycle
/// (<see cref="CommercialDistrictFactory"/>): a tapped dual land with an ETB
/// triggered ability — only here the ETB effect is the simple controller
/// life-gain "you gain 1 life" (CR 119.3), expressed declaratively as the
/// <c>gain_life_self</c> effect. Unconditional ETB-tapped (CR 614.1c) is
/// applied on the production load path by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (this factory builds
/// the land without it, for test convenience — matches the scry-land /
/// surveil-land posture).
/// </summary>
[CardName("Jwar Isle Refuge")]
public static class JwarIsleRefugeFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("jwar-isle-refuge");

    /// <summary>Construct Jwar Isle Refuge owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
