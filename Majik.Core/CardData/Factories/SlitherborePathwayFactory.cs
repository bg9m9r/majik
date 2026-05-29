using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Darkbore Pathway // Slitherbore Pathway (Kaldheim).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "{T}: Add {G}."
///
/// Front face — <see cref="DarkborePathwayFactory"/> (Land —
/// "{T}: Add {B}.").
///
/// ## MDFC infra
///
/// See <see cref="DarkborePathwayFactory"/>'s class doc for the
/// play-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as the back land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Slitherbore Pathway"</c> and lands here. The card is constructed with
/// its <see cref="MdfcState"/> pre-flipped to the back face so the face
/// tracker reads as authoritative (mirrors <see cref="SearstepPathwayFactory"/>).
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {G}</b> mana ability are loaded from the
/// embedded JSON definition (<c>slitherbore-pathway.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker is attached
/// in code.
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype/supertype.
///   Owner / controller wired (from JSON).
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
/// - <b>{T}: Add {G}</b> — single <see cref="ManaAbility"/> producing one
///   green mana (CR 605.1 — mana ability, no stack), from JSON.
/// - No enters-tapped clause (CR 305.4 — pathways enter untapped); no other
///   abilities.
/// </summary>
[CardName("Slitherbore Pathway")]
public static class SlitherborePathwayFactory
{
    public const string CardName = "Slitherbore Pathway";
    public const string FrontName = "Darkbore Pathway";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("slitherbore-pathway");

    /// <summary>
    /// Construct the back face Slitherbore Pathway owned and controlled by
    /// <paramref name="owner"/>. Identity + the {T}: Add {G} mana ability
    /// come from JSON; the MDFC face tracker (pre-flipped to the back face)
    /// is attached in code.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Slitherbore Pathway is the back face that actually exists
        // on the battlefield when this factory is dispatched).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
