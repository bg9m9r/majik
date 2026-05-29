using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Crystal Grotto (Dominaria United / March of the
/// Machine Commander).
///
/// Land. Oracle text:
///   "When this land enters, scry 1.
///    {T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/crystal-grotto.json</c>. Same JSON-driven
/// shape as the Theros scry-temples (<see cref="TempleOfTriumphFactory"/>)
/// — an <c>etb_self</c> → <c>scry_self 1</c> (CR 701.20) triggered ability
/// — but Crystal Grotto does <b>not</b> enter tapped, so no
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> step applies.
///
/// Mana abilities (CR 605.1):
/// <list type="bullet">
///   <item><c>{T}: Add {C}</c> — one cost-free colourless mana ability.</item>
///   <item><c>{1}, {T}: Add one mana of any color</c> — fanned out into
///     five per-colour <see cref="Majik.Core.Abilities.ManaAbility"/> slots
///     (W/U/B/R/G), each carrying the <c>{1}</c> additional mana cost via
///     the signet/filter-land "{N}, {T}: Add &lt;pips&gt;" overload
///     (<see cref="CardDefinitionFactory"/>). This is the same WUBRG
///     fan-out the engine uses for "any color" everywhere else (Springleaf
///     Drum, Aether Hub): the activator picks the colour by picking the
///     matching ability slot.</item>
/// </list>
///
/// Scry decision is agent-driven via
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseScryDecisionAsync"/>
/// when registered, otherwise the default all-to-bottom fall-back.
/// </summary>
[CardName("Crystal Grotto")]
public static class CrystalGrottoFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("crystal-grotto");

    /// <summary>Construct Crystal Grotto owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
