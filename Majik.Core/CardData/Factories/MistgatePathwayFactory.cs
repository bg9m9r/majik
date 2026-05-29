using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Hengegate Pathway // Mistgate Pathway (Kaldheim).
///
/// Land. Oracle text (back):
///   "{T}: Add {U}."
///
/// Front face — <see cref="HengegatePathwayFactory"/> (Land — "{T}: Add {W}.").
///
/// ## MDFC infra
/// See <see cref="HengegatePathwayFactory"/>'s class doc for the play-either-
/// face design. This factory is the back-face dispatch arm: when a player
/// chooses to play the MDFC as Mistgate Pathway, <see cref="NamedCardFactory"/>
/// resolves the back-face name <c>"Mistgate Pathway"</c> and lands here. The
/// card is constructed with its <see cref="MdfcState"/> pre-flipped to the
/// back face so the face tracker reads as authoritative — the same posture as
/// <see cref="MurkwaterPathwayFactory"/>.
///
/// ## Implemented (v1)
/// - Plain non-basic <see cref="Land"/> (no land subtype, no supertype),
///   declared declaratively in
///   <c>Majik.Core/CardData/Cards/mistgate-pathway.json</c> and materialized
///   via <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add {U}</b> — single mana ability producing one blue mana
///   (CR 605.1 — mana ability, no stack).
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
///
/// Neither Pathway face enters tapped and neither carries any non-mana
/// ability, so there is no replacement / trigger wiring to model.
/// </summary>
[CardName("Mistgate Pathway")]
public static class MistgatePathwayFactory
{
    public const string CardName = "Mistgate Pathway";
    public const string FrontName = "Hengegate Pathway";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("mistgate-pathway");

    /// <summary>Construct Mistgate Pathway (back face) owned and controlled
    /// by <paramref name="owner"/>, with its <see cref="MdfcState"/>
    /// pre-flipped to the back face.</summary>
    public static Land Create(Player owner)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Mistgate Pathway is the back face that actually exists on the
        // battlefield). Mirrors MurkwaterPathwayFactory.
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
