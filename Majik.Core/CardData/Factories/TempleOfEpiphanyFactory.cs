using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Epiphany (Theros Beyond Death — U/R "scry land").
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {U} or {R}."
///
/// <para>
/// Same oracle shape as the rest of the Theros/Born of the Gods/Journey into
/// Nyx/Theros Beyond Death "Temple" scry-land cycle (e.g. Temple of Deceit
/// {U}/{B}, Temple of Triumph {R}/{W}) — only the two produced colours differ.
/// Here they are {U} and {R} (CR 605.1a — mana abilities don't use the stack).
/// </para>
///
/// <para>
/// ## Card identity + abilities come from JSON
/// Name / Land type, the two single-colour <see cref="Majik.Core.Abilities.ManaAbility"/>s
/// ({U} and {R}), and the "When this land enters, scry 1" ETB
/// <see cref="Majik.Core.Abilities.TriggeredAbility"/> are all declared in the
/// embedded JSON definition (<c>temple-of-epiphany.json</c>) and materialised via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The ETB effect uses the standard
/// <c>scry_self</c> path (CR 701.20): with a registered
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> the controller decides
/// the bottom/top partition; otherwise the pre-agent default puts the peeked
/// card on the bottom. Same JSON-identity posture as
/// <see cref="TempleOfDeceitFactory"/>.
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c)
/// "This land enters tapped." is unconditional and is applied on the
/// production load path by <see cref="EntersTappedBinder"/> (it matches the
/// oracle line), NOT by this named factory — identical split to the rest of
/// the scry-land cycle (<see cref="TempleOfDeceitFactory"/>). The named factory
/// exists for the test / <see cref="NamedCardFactory"/> dispatch path so unit
/// tests get the mana + scry abilities without round-tripping through the
/// binder chain.
/// </para>
/// </summary>
[CardName("Temple of Epiphany")]
public static class TempleOfEpiphanyFactory
{
    public const string CardName = "Temple of Epiphany";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-epiphany");

    /// <summary>
    /// Construct Temple of Epiphany owned and controlled by <paramref name="owner"/>.
    /// Identity, the {U}/{R} mana abilities, and the ETB scry-1 trigger all
    /// come from the embedded JSON definition.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
