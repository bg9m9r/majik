using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced
/// "Pathway" land Cragcrown Pathway // Timbercrown Pathway (Kaldheim).
///
/// Land. Oracle text (back, verified against Scryfall — layout
/// <c>modal_dfc</c>):
///   "{T}: Add {G}."
///
/// Front face — <see cref="CragcrownPathwayFactory"/> (Land — "{T}: Add {R}.").
///
/// ## MDFC infra
/// See <see cref="CragcrownPathwayFactory"/>'s class doc for the play-either-
/// face design. This factory is the back-face dispatch arm: when a player
/// chooses to play the MDFC as Timbercrown Pathway,
/// <see cref="NamedCardFactory"/> resolves the back-face name
/// <c>"Timbercrown Pathway"</c> and lands here. The card is constructed with
/// its <see cref="MdfcState"/> pre-flipped to the back face so the face
/// tracker reads as authoritative (same posture as
/// <see cref="AgadeemTheUndercryptFactory"/>).
///
/// ## Card identity comes from JSON
/// Name / type and the <b>{T}: Add {G}</b> mana ability are loaded from the
/// embedded JSON definition (<c>timbercrown-pathway.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker is attached in
/// code (the JSON schema does not model faces).
///
/// ## Implemented (v1)
/// - Non-Basic <see cref="Land"/> with no printed subtype, no mana cost.
///   Owner / controller wired.
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (Timbercrown Pathway is the back face that exists on the battlefield
///   when the back is played).
/// - <b>{T}: Add {G}</b> — single <see cref="Majik.Core.Abilities.ManaAbility"/>
///   producing one green mana (CR 605.1 — mana ability, no stack), from JSON.
/// - <b>No ETB-tapped</b> — Pathway lands enter untapped, so no
///   <c>EntersTappedReplacement</c> is attached.
/// </summary>
[CardName("Timbercrown Pathway")]
public static class TimbercrownPathwayFactory
{
    public const string CardName = "Timbercrown Pathway";
    public const string FrontName = "Cragcrown Pathway";

    /// <summary>Construct Timbercrown Pathway owned and controlled by
    /// <paramref name="owner"/>. Identity + the {T}: Add {G} mana ability
    /// come from JSON; the MDFC face tracker is attached pre-flipped to the
    /// back face.</summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Identity + the {T}: Add {G} mana ability come from JSON.
        var definition = CardDefinitionLoader.FromEmbeddedResource("timbercrown-pathway");
        var land = (Land)CardDefinitionFactory.Build(definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Timbercrown Pathway is the back face that actually
        // exists on the battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
