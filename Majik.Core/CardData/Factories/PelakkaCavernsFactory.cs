using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Pelakka Predation // Pelakka Caverns (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {B}."
///
/// Front face — <see cref="PelakkaPredationFactory"/> (Sorcery {2}{B}).
///
/// ## MDFC infra
///
/// Mirrors <see cref="MalakirMireFactory"/> (the companion ZNR black MDFC
/// back-face land). When a player chooses to play the MDFC as a land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Pelakka Caverns"</c> and lands here. The card is constructed with its
/// <see cref="MdfcState"/> pre-flipped to the back face so the face tracker
/// reads as authoritative.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {B}</b> mana ability are loaded from the
/// embedded JSON definition (<c>pelakka-caverns.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker is attached in
/// code (the JSON schema models neither MDFC faces nor enters-tapped).
///
/// ## Enters tapped (CR 614.1c)
///
/// Pelakka Caverns ENTERS TAPPED unconditionally. On the production load path
/// the <see cref="Majik.Core.CardData.EntersTappedBinder"/> recognises the
/// printed "This land enters tapped." sentence and registers an
/// <c>EntersTappedReplacement</c>. This factory builds the land WITHOUT that
/// replacement — matching the test-convenience posture of
/// <see cref="MalakirMireFactory"/>.
/// </summary>
[CardName("Pelakka Caverns")]
public static class PelakkaCavernsFactory
{
    public const string CardName = "Pelakka Caverns";
    public const string FrontName = "Pelakka Predation";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("pelakka-caverns");

    /// <summary>
    /// Construct Pelakka Caverns owned and controlled by
    /// <paramref name="owner"/>. Identity + the {T}: Add {B} mana ability come
    /// from JSON; the <see cref="MdfcState"/> is pre-flipped to the back face.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Pelakka Caverns is the back face that actually exists on the
        // battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
