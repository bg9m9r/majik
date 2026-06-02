using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Mystery (Theros Beyond Death — G/U "scry land").
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {G} or {U}."
///
/// <para>
/// Same oracle shape as the rest of the Theros "Temple" scry-land cycle
/// (e.g. <see cref="TempleOfDeceitFactory"/> {U}/{B}, Temple of Triumph
/// {R}/{W}) — only the two produced colours differ. Here they are {G} and
/// {U} (CR 605.1a — mana abilities don't use the stack).
/// </para>
///
/// <para>
/// ## Card identity + abilities come from JSON
/// Name / Land type, the two single-colour <see cref="Majik.Core.Abilities.ManaAbility"/>s
/// ({G} and {U}), and the "When this land enters, scry 1" ETB
/// <see cref="Majik.Core.Abilities.TriggeredAbility"/> are all declared in the
/// embedded JSON definition (<c>temple-of-mystery.json</c>) and materialised via
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
/// the scry-land cycle. The named factory exists for the test /
/// <see cref="NamedCardFactory"/> dispatch path so unit tests get the mana +
/// scry abilities without round-tripping through the binder chain.
/// </para>
/// </summary>
[CardName("Temple of Mystery")]
public static class TempleOfMysteryFactory
{
    public const string CardName = "Temple of Mystery";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-mystery");

    /// <summary>
    /// Construct Temple of Mystery owned and controlled by <paramref name="owner"/>.
    /// Identity, the {G}/{U} mana abilities, and the ETB scry-1 trigger all
    /// come from the embedded JSON definition.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
