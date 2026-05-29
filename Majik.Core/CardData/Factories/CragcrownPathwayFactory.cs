using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced
/// "Pathway" land Cragcrown Pathway // Timbercrown Pathway (Kaldheim).
///
/// Land. Oracle text (front, verified against Scryfall — layout
/// <c>modal_dfc</c>):
///   "{T}: Add {R}."
///
/// Back face — <see cref="TimbercrownPathwayFactory"/> (Land — "{T}: Add {G}.").
///
/// ## MDFC infra (CR 711 / 712)
/// Modal double-faced card: each printed face has its own complete
/// characteristics; the controller chooses which face to play. Each face is
/// given its own <c>[CardName]</c>-dispatched factory:
/// <list type="bullet">
///   <item>Playing the front face → <see cref="NamedCardFactory"/> resolves
///     <c>"Cragcrown Pathway"</c> → this factory → a red-tapland-style
///     <see cref="Land"/> (untapped) with <c>{T}: Add {R}</c>.</item>
///   <item>Playing the back face → <see cref="NamedCardFactory"/> resolves
///     <c>"Timbercrown Pathway"</c> → <see cref="TimbercrownPathwayFactory"/>
///     → a <see cref="Land"/> with <c>{T}: Add {G}</c>.</item>
/// </list>
/// The combined seed name <c>"Cragcrown Pathway // Timbercrown Pathway"</c>
/// flips to <c>IsImplemented</c> via the front-face check in
/// <see cref="EmbeddedCardRepository.DeriveImplemented"/>.
///
/// ## Card identity comes from JSON
/// Name / type and the <b>{T}: Add {R}</b> mana ability are loaded from the
/// embedded JSON definition (<c>cragcrown-pathway.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker is attached in
/// code (the JSON schema does not model faces).
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/> with no printed subtype, no mana cost.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached on the front face (NOT flipped —
///   Cragcrown Pathway is the front face that exists on the battlefield when
///   the front is played).
/// - <b>{T}: Add {R}</b> — single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   producing one red mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>No ETB-tapped</b> — Pathway lands enter untapped (no
///   "enters tapped" clause on the Scryfall oracle text), so no
///   <c>EntersTappedReplacement</c> is attached. This is what makes the
///   Pathway cycle strictly simpler than the painland / tapland MDFC
///   back-faces (Bala Ged Sanctuary, Agadeem the Undercrypt, etc.).
/// </summary>
[CardName("Cragcrown Pathway")]
public static class CragcrownPathwayFactory
{
    public const string CardName = "Cragcrown Pathway";
    public const string BackName = "Timbercrown Pathway";

    /// <summary>Construct Cragcrown Pathway owned and controlled by
    /// <paramref name="owner"/>. Identity + the {T}: Add {R} mana ability
    /// come from JSON; the MDFC face tracker is attached on the front
    /// face.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {R} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("cragcrown-pathway");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker on the front face
        // (Cragcrown Pathway is the front face). Not flipped.
        land.MdfcState = new MdfcState(CardName, BackName);

        return land;
    }
}
