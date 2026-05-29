using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Barkchannel Pathway // Tidechannel Pathway (Kaldheim "Pathway" dual-land
/// cycle).
///
/// Land. Oracle text (front, verified against Scryfall):
///   "{T}: Add {G}."
///
/// Back face — <see cref="TidechannelPathwayFactory"/> (Land — "{T}: Add {U}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// A Pathway is a modal double-faced land: at play time the controller
/// chooses which face to put onto the battlefield (CR 712.6 / 305.1).
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="BranchloftPathwayFactory"/> / <see cref="BoulderloftPathwayFactory"/>.
/// Both Pathway faces are plain "{T}: Add &lt;C&gt;" lands with no ETB-tapped
/// clause and no other text — so neither face needs a
/// <see cref="Majik.Core.Abilities.ReplacementEffect"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {G}</b> mana ability are loaded from the
/// embedded JSON definition (<c>barkchannel-pathway.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker is attached in code (the JSON schema models no MDFC faces).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype. Owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached (front = "Barkchannel Pathway",
///   back = "Tidechannel Pathway"); starts on the front face.
/// - <b>{T}: Add {G}</b> — single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   producing one green mana (CR 605.1 — mana ability, no stack), from JSON.
/// </summary>
[CardName("Barkchannel Pathway")]
public static class BarkchannelPathwayFactory
{
    public const string CardName = "Barkchannel Pathway";
    public const string BackName = "Tidechannel Pathway";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("barkchannel-pathway");

    /// <summary>
    /// Construct Barkchannel Pathway (front face) owned and controlled by
    /// <paramref name="owner"/>. Identity + the {T}: Add {G} mana ability come
    /// from JSON; the <see cref="MdfcState"/> face tracker is attached on the
    /// front face.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker so the printed back-face
        // name (Tidechannel Pathway) is observable from the front-face card
        // object. Starts on the front face.
        land.MdfcState = new MdfcState(CardName, BackName);

        return land;
    }
}
