using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Malice (Theros Beyond Death).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {B} or {R}."
///
/// Scryfall type line: Land (no basic supertype, no subtype). A B/R member of
/// the Theros "scry land" cycle — same oracle shape as Temple of Triumph /
/// Temple of Deceit etc.; only the produced colours differ ({B}/{R} here).
///
/// ## Card identity + abilities come from JSON
///
/// Name / type, the two single-colour mana abilities (<b>{T}: Add {B}</b> and
/// <b>{T}: Add {R}</b>, CR 605.1a), and the <b>"When this land enters,
/// scry 1"</b> ETB triggered ability are all loaded from the embedded JSON
/// definition (<c>temple-of-malice.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The scry uses the standard
/// <c>scry_self</c> path (CR 701.20 — scry keyword action): when an
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> is registered the
/// controller decides the bottom/top partition; otherwise the pre-agent
/// default puts the single peeked card on the bottom. Same JSON-driven
/// posture as <see cref="CastleVantressFactory"/>.
///
/// ## Enters tapped (CR 614.1c)
///
/// "This land enters tapped." is an <b>unconditional</b> ETB-tapped
/// replacement. On the production load path it is applied by
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> (matches the
/// "This land enters tapped." oracle line), not wired in this named factory —
/// the same split as the surveil-land cycle. The test/dispatcher path
/// (<see cref="NamedCardFactory"/>) constructs identity + abilities only.
/// </summary>
[CardName("Temple of Malice")]
public static class TempleOfMaliceFactory
{
    public const string CardName = "Temple of Malice";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-malice");

    /// <summary>
    /// Construct Temple of Malice owned and controlled by
    /// <paramref name="owner"/>. Identity, the two single-colour mana
    /// abilities, and the ETB scry-1 triggered ability all come from JSON.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
