using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Sejiri Shelter // Sejiri Glacier (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {W}."
///
/// Front face — <see cref="SejiriShelterFactory"/> (Instant {1}{W}).
///
/// ## MDFC infra
///
/// See <see cref="SejiriShelterFactory"/>'s class doc for the cast-either-face
/// design. This factory is the back-face dispatch arm: when a player chooses
/// to play the MDFC as a land, <see cref="NamedCardFactory"/> resolves the
/// back-face name <c>"Sejiri Glacier"</c> and lands here. The card is
/// constructed with its <see cref="MdfcState"/> pre-flipped to the back face
/// so the face tracker reads as authoritative — same posture as
/// <see cref="MalakirMireFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {W}</b> mana ability are loaded from the
/// embedded JSON definition (<c>sejiri-glacier.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker is attached in
/// code (the JSON schema models neither MDFC faces nor enters-tapped).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype.
///   Owner / controller wired (from JSON).
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="MalakirMireFactory"/>).
/// - <b>{T}: Add {W}</b> — single mana ability producing one white mana
///   (CR 605.1 — mana ability, no stack), from JSON.
///
/// ## Enters tapped (CR 614.1c)
///
/// Sejiri Glacier ENTERS TAPPED unconditionally. On the production load path
/// the <see cref="Majik.Core.CardData.EntersTappedBinder"/> recognises the
/// printed "This land enters tapped." sentence and registers an
/// <c>EntersTappedReplacement</c>. This factory builds the land WITHOUT that
/// replacement — matching the test-convenience posture of
/// <see cref="MalakirMireFactory"/> and the tapped-dual cycle factories.
///
/// ## References
///
/// - <see cref="MalakirMireFactory"/> — companion ZNR MDFC back-face land
///   (JSON-loaded {T}: Add mana + code-attached pre-flipped MdfcState).
/// </summary>
[CardName("Sejiri Glacier")]
public static class SejiriGlacierFactory
{
    public const string CardName = "Sejiri Glacier";
    public const string FrontName = "Sejiri Shelter";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sejiri-glacier");

    /// <summary>
    /// Construct Sejiri Glacier owned and controlled by <paramref name="owner"/>.
    /// Identity + the {T}: Add {W} mana ability come from JSON; the
    /// <see cref="MdfcState"/> is pre-flipped to the back face.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Sejiri Glacier is the back face that actually exists on the
        // battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
