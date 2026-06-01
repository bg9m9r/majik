using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Deceit (Theros — U/B "scry land").
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {U} or {B}."
///
/// <para>
/// Same oracle shape as the rest of the Theros/Born of the Gods/Journey into
/// Nyx "Temple" scry-land cycle (e.g. Temple of Epiphany {U}/{R}, Temple of
/// Triumph {R}/{W}) — only the two produced colours differ. Here they are
/// {U} and {B} (CR 605.1a — mana abilities don't use the stack).
/// </para>
///
/// <para>
/// ## Card identity + abilities come from JSON
/// Name / Land type, the two single-colour <see cref="Majik.Core.Abilities.ManaAbility"/>s
/// ({U} and {B}), and the "When this land enters, scry 1" ETB
/// <see cref="Majik.Core.Abilities.TriggeredAbility"/> are all declared in the
/// embedded JSON definition (<c>temple-of-deceit.json</c>) and materialised via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The ETB effect uses the standard
/// <c>scry_self</c> path (CR 701.20): with a registered
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> the controller decides
/// the bottom/top partition; otherwise the pre-agent default puts the peeked
/// card on the bottom. Same JSON-identity posture as
/// <see cref="CastleVantressFactory"/>.
/// </para>
///
/// <para>
/// ## Enters tapped (CR 614.1c)
/// "This land enters tapped." is unconditional and is applied on the
/// production load path by <see cref="EntersTappedBinder"/> (it matches the
/// oracle line), NOT by this named factory — identical split to the surveil-
/// land cycle (<see cref="SurveilLandCycleFactory"/>). The named factory
/// exists for the test / <see cref="NamedCardFactory"/> dispatch path so unit
/// tests get the mana + scry abilities without round-tripping through the
/// binder chain.
/// </para>
/// </summary>
[CardName("Temple of Deceit")]
public static class TempleOfDeceitFactory
{
    public const string CardName = "Temple of Deceit";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-deceit");

    /// <summary>
    /// Construct Temple of Deceit owned and controlled by <paramref name="owner"/>.
    /// Identity, the {U}/{B} mana abilities, and the ETB scry-1 trigger all
    /// come from the embedded JSON definition.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
