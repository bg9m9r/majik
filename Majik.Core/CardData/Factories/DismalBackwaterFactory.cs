using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Dismal Backwater (Khans of Tarkir) — the U/B
/// member of the "gain land" / refuge dual-land cycle.
///
/// Oracle text (Scryfall, verified):
///   "This land enters tapped.
///    When this land enters, you gain 1 life.
///    {T}: Add {U} or {B}."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/dismal-backwater.json</c> and lets
/// <see cref="CardDefinitionFactory"/> build the runtime card. Shares the
/// dual-land + ETB-trigger shape of the surveil-land cycle (e.g.
/// <see cref="CommercialDistrictFactory"/>), substituting an ETB
/// <c>gain_life_self</c> effect for the surveil effect.
///
/// - Two single-colour mana abilities — <c>{T}: Add {U} or {B}</c>
///   (CR 605.1 — mana abilities, never use the stack).
/// - ETB triggered ability (CR 603.6a) gaining the controller 1 life via
///   the JSON <c>gain_life_self</c> effect.
/// - Unconditional ETB-tapped (CR 614.1c) is applied on the production
///   load path by <see cref="Majik.Core.CardData.EntersTappedBinder"/>
///   from the printed oracle text ("This land enters tapped."), not by
///   this factory — matching the surveil-land cycle's shape-only posture.
/// </summary>
[CardName("Dismal Backwater")]
public static class DismalBackwaterFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("dismal-backwater");

    /// <summary>Construct Dismal Backwater owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
