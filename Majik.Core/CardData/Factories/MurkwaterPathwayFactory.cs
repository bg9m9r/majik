using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Clearwater Pathway // Murkwater Pathway (Zendikar Rising).
///
/// Land. Oracle text (back):
///   "{T}: Add {B}."
///
/// Front face — <see cref="ClearwaterPathwayFactory"/> (Land — "{T}: Add {U}.").
///
/// ## MDFC infra
/// See <see cref="ClearwaterPathwayFactory"/>'s class doc for the play-either-
/// face design. This factory is the back-face dispatch arm: when a player
/// chooses to play the MDFC as Murkwater Pathway, <see cref="NamedCardFactory"/>
/// resolves the back-face name <c>"Murkwater Pathway"</c> and lands here. The
/// card is constructed with its <see cref="MdfcState"/> pre-flipped to the
/// back face so the face tracker reads as authoritative — the same posture as
/// <see cref="WitchBlessedMeadowFactory"/> / <see cref="RazorgrassFieldFactory"/>.
///
/// ## Implemented (v1)
/// - Plain non-basic <see cref="Land"/> (no land subtype, no supertype),
///   declared declaratively in
///   <c>Majik.Core/CardData/Cards/murkwater-pathway.json</c> and materialized
///   via <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add {B}</b> — single mana ability producing one black mana
///   (CR 605.1 — mana ability, no stack).
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
///
/// Neither Pathway face enters tapped and neither carries any non-mana
/// ability, so there is no replacement / trigger wiring to model.
/// </summary>
[CardName("Murkwater Pathway")]
public static class MurkwaterPathwayFactory
{
    public const string CardName = "Murkwater Pathway";
    public const string FrontName = "Clearwater Pathway";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("murkwater-pathway");

    /// <summary>Construct Murkwater Pathway (back face) owned and controlled
    /// by <paramref name="owner"/>, with its <see cref="MdfcState"/>
    /// pre-flipped to the back face.</summary>
    public static Land Create(Player owner)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Murkwater Pathway is the back face that actually exists on the
        // battlefield). Mirrors WitchBlessedMeadowFactory / RazorgrassFieldFactory.
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
