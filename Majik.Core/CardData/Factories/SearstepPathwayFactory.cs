using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Blightstep Pathway // Searstep Pathway (Kaldheim).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "{T}: Add {R}."
///
/// Front face — <see cref="BlightstepPathwayFactory"/> (Land —
/// "{T}: Add {B}.").
///
/// ## MDFC infra
///
/// See <see cref="BlightstepPathwayFactory"/>'s class doc for the
/// play-either-face design. This factory is the back-face dispatch arm:
/// when a player chooses to play the MDFC as the back land,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Searstep Pathway"</c> and lands here. The card is constructed with
/// its <see cref="MdfcState"/> pre-flipped to the back face so the face
/// tracker reads as authoritative (mirrors
/// <see cref="BalaGedSanctuaryFactory"/> / <see cref="AgadeemTheUndercryptFactory"/>).
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {R}</b> mana ability are loaded from the
/// embedded JSON definition (<c>searstep-pathway.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker is attached
/// in code.
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype/supertype.
///   Owner / controller wired (from JSON).
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face.
/// - <b>{T}: Add {R}</b> — single <see cref="ManaAbility"/> producing one
///   red mana (CR 605.1 — mana ability, no stack), from JSON.
/// - No enters-tapped clause (CR 305.4 — pathways enter untapped); no other
///   abilities.
/// </summary>
[CardName("Searstep Pathway")]
public static class SearstepPathwayFactory
{
    public const string CardName = "Searstep Pathway";
    public const string FrontName = "Blightstep Pathway";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("searstep-pathway");

    /// <summary>
    /// Construct the back face Searstep Pathway owned and controlled by
    /// <paramref name="owner"/>. Identity + the {T}: Add {R} mana ability
    /// come from JSON; the MDFC face tracker (pre-flipped to the back face)
    /// is attached in code.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Searstep Pathway is the back face that actually exists
        // on the battlefield when this factory is dispatched).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
