using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Blightstep Pathway // Searstep Pathway (Kaldheim).
///
/// Land. Oracle text (front, verified against Scryfall):
///   "{T}: Add {B}."
///
/// Back face — <see cref="SearstepPathwayFactory"/> (Land — "{T}: Add {R}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Pathways are a land // land modal double-faced cycle: each printed face
/// is a complete plain <see cref="Land"/> with a single intrinsic
/// <c>{T}: Add {C}</c> mana ability and no enters-tapped clause. At play
/// time the controller chooses which face to put onto the battlefield
/// (CR 712.10). Modelled by giving each printed face its own
/// <c>[CardName]</c>-dispatched factory:
/// <list type="bullet">
///   <item>Playing the front face → <see cref="NamedCardFactory"/> resolves
///     <c>"Blightstep Pathway"</c> → this factory → a <see cref="Land"/>
///     with <c>{T}: Add {B}</c>.</item>
///   <item>Playing the back face → <see cref="NamedCardFactory"/> resolves
///     <c>"Searstep Pathway"</c> → <see cref="SearstepPathwayFactory"/> →
///     a <see cref="Land"/> with <c>{T}: Add {R}</c>.</item>
/// </list>
/// Both face cards carry an <see cref="MdfcState"/> tracker; the front-face
/// card starts on the front face.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {B}</b> mana ability are loaded from the
/// embedded JSON definition (<c>blightstep-pathway.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker is attached
/// in code (the JSON schema does not model it). This is the same thin
/// JSON-driven shape as <see cref="SeachromeCoastFactory"/>, plus the MDFC
/// tracker the pathway cycle needs.
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype/supertype.
///   Owner / controller wired (from JSON).
/// - <see cref="MdfcState"/> attached (front = "Blightstep Pathway",
///   back = "Searstep Pathway"); starts on the front face.
/// - <b>{T}: Add {B}</b> — single <see cref="ManaAbility"/> producing one
///   black mana (CR 605.1 — mana ability, no stack), from JSON.
/// - No enters-tapped clause (CR 305.4 — pathways enter untapped); no other
///   abilities. A land's mana ability does not make it a coloured permanent
///   (CR 202.2 — colour comes from colour indicator / mana cost, neither of
///   which a pathway has).
/// </summary>
[CardName("Blightstep Pathway")]
public static class BlightstepPathwayFactory
{
    public const string CardName = "Blightstep Pathway";
    public const string BackName = "Searstep Pathway";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("blightstep-pathway");

    /// <summary>
    /// Construct the front face Blightstep Pathway owned and controlled by
    /// <paramref name="owner"/>. Identity + the {T}: Add {B} mana ability
    /// come from JSON; the MDFC face tracker (starting on the front face) is
    /// attached in code.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker. The front-face card
        // starts on the front face.
        land.MdfcState = new MdfcState(CardName, BackName);

        return land;
    }
}
