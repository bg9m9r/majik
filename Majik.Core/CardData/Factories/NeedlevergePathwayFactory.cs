using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Needleverge Pathway // Pillarverge Pathway (Streets of New Capenna).
///
/// Land. Oracle text (front):
///   "{T}: Add {R}."
///
/// Back face — <see cref="PillarvergePathwayFactory"/> (Land — "{T}: Add {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
/// Modal Double-Faced Card: each printed face is a complete land with its
/// own characteristics. At play time the controller chooses which face to
/// put onto the battlefield. Modelled by giving each printed face its own
/// <c>[CardName]</c>-dispatched factory — the same posture as the Kaldheim
/// pathway pair <see cref="HengegatePathwayFactory"/> /
/// <see cref="MistgatePathwayFactory"/>, where both faces are plain lands:
/// <list type="bullet">
///   <item>Playing the front face → <see cref="NamedCardFactory"/> resolves
///     <c>"Needleverge Pathway"</c> → this factory → a <see cref="Land"/>
///     with a single {T}: Add {R} mana ability.</item>
///   <item>Playing the back face → <see cref="NamedCardFactory"/> resolves
///     <c>"Pillarverge Pathway"</c> → <see cref="PillarvergePathwayFactory"/>
///     → a <see cref="Land"/> with a single {T}: Add {W} mana ability.</item>
/// </list>
/// Both face cards carry an <see cref="MdfcState"/> tracker; this front-face
/// card starts on the front face.
///
/// ## Implemented (v1)
/// - Plain non-basic <see cref="Land"/> (no land subtype, no supertype),
///   declared declaratively in
///   <c>Majik.Core/CardData/Cards/needleverge-pathway.json</c> and
///   materialized via <see cref="CardDefinitionFactory"/>.
/// - <b>{T}: Add {R}</b> — single mana ability producing one red mana
///   (CR 605.1 — mana ability, no stack).
/// - <see cref="MdfcState"/> attached (front = "Needleverge Pathway", back =
///   "Pillarverge Pathway"); starts on the front face.
///
/// Neither Pathway face enters tapped and neither carries any non-mana
/// ability, so there is no replacement / trigger wiring to model.
/// </summary>
[CardName("Needleverge Pathway")]
public static class NeedlevergePathwayFactory
{
    public const string CardName = "Needleverge Pathway";
    public const string BackName = "Pillarverge Pathway";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("needleverge-pathway");

    /// <summary>Construct Needleverge Pathway (front face) owned and
    /// controlled by <paramref name="owner"/>.</summary>
    public static Land Create(Player owner)
    {
        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed
        // back-face name (Pillarverge Pathway) is observable from the
        // front-face card object. Starts on the front face.
        land.MdfcState = new MdfcState(CardName, BackName);

        return land;
    }
}
