using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Branchloft Pathway // Boulderloft Pathway (Zendikar Rising "Pathway"
/// dual-land cycle).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "{T}: Add {W}."
///
/// Front face — <see cref="BranchloftPathwayFactory"/> (Land — "{T}: Add {G}.").
///
/// ## MDFC infra
///
/// See <see cref="BranchloftPathwayFactory"/>'s class doc for the
/// cast-either-face design. This factory is the back-face dispatch arm: when
/// a player chooses to play the Pathway as Boulderloft Pathway,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Boulderloft Pathway"</c> and lands here. The card is constructed with
/// its <see cref="MdfcState"/> pre-flipped to the back face so the face
/// tracker reads as authoritative (mirrors
/// <see cref="AgadeemTheUndercryptFactory"/>).
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {W}</b> mana ability are loaded from the
/// embedded JSON definition (<c>boulderloft-pathway.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>.
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (Boulderloft Pathway is the back face that actually exists on the
///   battlefield).
/// - <b>{T}: Add {W}</b> — single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   producing one white mana (CR 605.1 — mana ability, no stack), from JSON.
/// </summary>
[CardName("Boulderloft Pathway")]
public static class BoulderloftPathwayFactory
{
    public const string CardName = "Boulderloft Pathway";
    public const string FrontName = "Branchloft Pathway";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("boulderloft-pathway");

    /// <summary>
    /// Construct Boulderloft Pathway (back face) owned and controlled by
    /// <paramref name="owner"/>. Identity + the {T}: Add {W} mana ability come
    /// from JSON; the <see cref="MdfcState"/> face tracker is attached
    /// pre-flipped to the back face.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the back
        // face (Boulderloft Pathway is the back face that actually exists on
        // the battlefield). Mirrors AgadeemTheUndercryptFactory.
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
