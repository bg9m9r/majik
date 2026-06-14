using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Temple of Plenty (Born of the Gods).
///
/// Land. Oracle text (Scryfall-confirmed):
///   "This land enters tapped.
///    When this land enters, scry 1. (Look at the top card of your library.
///    You may put that card on the bottom.)
///    {T}: Add {G} or {W}."
///
/// Scryfall type line: Land (no basic supertype, no subtype). A G/W member of
/// the Theros "scry land" cycle — same oracle shape as Temple of Abandon /
/// Temple of Malice etc.; only the produced colours differ ({G}/{W} here).
///
/// ## Card identity + abilities come from JSON
///
/// Name / type, the two single-colour mana abilities (<b>{T}: Add {G}</b> and
/// <b>{T}: Add {W}</b>, CR 605.1a), and the <b>"When this land enters,
/// scry 1"</b> ETB triggered ability are all loaded from the embedded JSON
/// definition (<c>temple-of-plenty.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The scry uses the standard
/// <c>scry_self</c> path (CR 701.20 — scry keyword action): when an
/// <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> is registered the
/// controller decides the bottom/top partition; otherwise the pre-agent
/// default puts the single peeked card on the bottom. Same JSON-driven
/// posture as <see cref="TempleOfAbandonFactory"/>.
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
[CardName("Temple of Plenty")]
public static class TempleOfPlentyFactory
{
    public const string CardName = "Temple of Plenty";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("temple-of-plenty");

    /// <summary>
    /// Construct Temple of Plenty owned and controlled by
    /// <paramref name="owner"/>. Identity, the two single-colour mana
    /// abilities, and the ETB scry-1 triggered ability all come from JSON.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        return (Land)CardDefinitionFactory.Build(Definition, owner);
    }
}
