using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the BACK face of the modal double-faced card
/// Malakir Rebirth // Malakir Mire (Zendikar Rising).
///
/// Land. Oracle text (back, verified against Scryfall):
///   "This land enters tapped."
///   "{T}: Add {B}."
///
/// Front face — <see cref="MalakirRebirthFactory"/> (Instant {B}).
///
/// ## MDFC infra
///
/// See <see cref="MalakirRebirthFactory"/>'s class doc for the cast-either-face
/// design. This factory is the back-face dispatch arm: when a player chooses
/// to play the MDFC as a land, <see cref="NamedCardFactory"/> resolves the
/// back-face name <c>"Malakir Mire"</c> and lands here. The card is
/// constructed with its <see cref="MdfcState"/> pre-flipped to the back face
/// so the face tracker reads as authoritative — same posture as
/// <see cref="AgadeemTheUndercryptFactory"/>.
///
/// ## Card identity comes from JSON
///
/// Name / type and the <b>{T}: Add {B}</b> mana ability are loaded from the
/// embedded JSON definition (<c>malakir-mire.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The MDFC face tracker is attached in
/// code (the JSON schema models neither MDFC faces nor enters-tapped).
///
/// ## Implemented (v1)
///
/// - Non-Basic <see cref="Land"/> with no printed subtype.
///   Owner / controller wired (from JSON).
/// - <see cref="MdfcState"/> attached, pre-flipped to the back face
///   (mirrors <see cref="AgadeemTheUndercryptFactory"/>).
/// - <b>{T}: Add {B}</b> — single mana ability producing one black mana
///   (CR 605.1 — mana ability, no stack), from JSON.
///
/// ## Enters tapped (CR 614.1c)
///
/// Unlike Agadeem, the Undercrypt ("you may pay 3 life; if you don't, it
/// enters tapped"), Malakir Mire ENTERS TAPPED unconditionally. On the
/// production load path the
/// <see cref="Majik.Core.CardData.EntersTappedBinder"/> recognises the
/// printed "This land enters tapped." sentence and registers an
/// <c>EntersTappedReplacement</c>. This factory builds the land WITHOUT that
/// replacement — matching the test-convenience posture of the tapped-dual
/// cycle factories (<see cref="JwarIsleRefugeFactory"/>,
/// <see cref="AgadeemTheUndercryptFactory"/>'s no-bus overload).
///
/// ## References
///
/// - <see cref="AgadeemTheUndercryptFactory"/> — companion ZNR black MDFC
///   back-face land (JSON-loaded {T}: Add {B} + code-attached MdfcState).
/// - <see cref="JwarIsleRefugeFactory"/> — tapped dual land whose
///   enters-tapped is applied by the production binder, not the factory.
/// </summary>
[CardName("Malakir Mire")]
public static class MalakirMireFactory
{
    public const string CardName = "Malakir Mire";
    public const string FrontName = "Malakir Rebirth";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("malakir-mire");

    /// <summary>
    /// Construct Malakir Mire owned and controlled by <paramref name="owner"/>.
    /// Identity + the {T}: Add {B} mana ability come from JSON; the
    /// <see cref="MdfcState"/> is pre-flipped to the back face.
    /// </summary>
    public static Land Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var land = (Land)CardDefinitionFactory.Build(Definition, owner);

        // CR 711 / 712 — attach the MDFC face tracker pre-flipped to the
        // back face (Malakir Mire is the back face that actually exists on
        // the battlefield).
        var mdfc = new MdfcState(FrontName, CardName);
        mdfc.Transform();
        land.MdfcState = mdfc;

        return land;
    }
}
