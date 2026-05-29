using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Hidden Grotto (Foundations).
///
/// Land. Oracle text:
///   "When this land enters, surveil 1.
///    {T}: Add {C}.
///    {1}, {T}: Add one mana of any color."
///
/// Thin wrapper that loads
/// <c>Majik.Core/CardData/Cards/hidden-grotto.json</c>. Mechanically
/// identical to <see cref="CrystalGrottoFactory"/> (Dominaria United filter
/// land) except the ETB trigger surveils 1 (CR 701.50) instead of scrying.
/// Hidden Grotto does <b>not</b> enter tapped, so no
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
///     fan-out the engine uses for "any color" everywhere else (Crystal
///     Grotto, Springleaf Drum, Aether Hub): the activator picks the colour
///     by picking the matching ability slot.</item>
/// </list>
///
/// Surveil decision is agent-driven via
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent.ChooseSurveilDecisionAsync"/>
/// when registered, otherwise the default all-to-graveyard fall-back.
/// </summary>
[CardName("Hidden Grotto")]
public static class HiddenGrottoFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("hidden-grotto");

    /// <summary>Construct Hidden Grotto owned and controlled by
    /// <paramref name="owner"/>.</summary>
    public static Land Create(Player owner) =>
        (Land)CardDefinitionFactory.Build(Definition, owner);
}
