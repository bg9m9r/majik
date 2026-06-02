using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Skyclave Cleric // Skyclave Basilica (Zendikar Rising, {1}{W}).
///
/// Creature — Kor Cleric 1/3. Oracle text (front, verified against Scryfall):
///   "When this creature enters, you gain 2 life."
///
/// Back face — <see cref="SkyclaveBasilicaFactory"/> (Land — "This land
/// enters tapped." / "{T}: Add {W}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="AkoumWarriorFactory"/> / <see cref="AkoumTeethFactory"/> (ZNR
/// creature-front + tapland-back MDFC). The front-face card carries a
/// castable <see cref="MdfcFace.Land"/> back-face descriptor on its
/// <see cref="MdfcState"/> so <see cref="Majik.Core.Game.MdfcCastFlow"/> can
/// offer the controller a face choice at play time and materialize a fresh
/// back-face land instance (Skyclave Basilica) when chosen. No transform
/// happens — only the chosen face exists (CR 712.4).
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost / 1/3 P/T / Kor Cleric subtypes AND the ETB
/// "you gain 2 life" triggered ability are loaded from the embedded JSON
/// definition (<c>skyclave-cleric.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The JSON's <c>etb_self</c> trigger +
/// <c>gain_life_self</c> effect (amount 2) materialize the ETB ability — the
/// same declarative shape the Refuge gain-life taplands use
/// (<see cref="AkoumRefugeFactory"/>). The <see cref="MdfcState"/> face
/// tracker is attached in code (the JSON schema models no MDFC faces).
///
/// ## Implemented (v1)
///
/// - 1/3 Creature — Kor Cleric, mana cost {1}{W}, owner / controller wired,
///   white (from the {W} pip per CR 202.2c).
/// - <see cref="MdfcState"/> attached (front = "Skyclave Cleric", back =
///   "Skyclave Basilica") with a castable <see cref="MdfcFace.Land"/> back
///   face; starts on the front face.
/// - <b>ETB triggered ability (CR 603.6a)</b> — "When this creature enters,
///   you gain 2 life." (CR 119.3). Battlefield-active <c>etb_self</c> trigger
///   with a single <c>gain_life_self</c> effect (amount 2), from JSON.
///
/// ## References
///
/// - <see cref="AkoumWarriorFactory"/> — companion ZNR creature-front MDFC
///   with the same castable-land-back MdfcState shape; this factory mirrors
///   its MDFC wiring (the ETB body is a gain-life trigger instead of a
///   Trample keyword marker).
/// - <see cref="AkoumRefugeFactory"/> — JSON-declared <c>etb_self</c> +
///   <c>gain_life_self</c> ETB gain-life shape this front face reuses.
/// </summary>
[CardName("Skyclave Cleric")]
public static class SkyclaveClericFactory
{
    public const string CardName = "Skyclave Cleric";
    public const string BackName = "Skyclave Basilica";
    public const string Slug = "skyclave-cleric";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Skyclave Cleric. Identity (name / Creature / Kor Cleric
    /// subtypes / {1}{W} / 1/3) and the ETB "you gain 2 life" trigger come
    /// from the embedded JSON definition; the <see cref="MdfcState"/> with the
    /// castable land back face is layered on in code. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape + ETB gain-2-life trigger from the embedded JSON
        // definition. Only the MDFC face tracker is layered on in code.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 712.3 / 712.4 — attach the MDFC face tracker WITH a castable
        // back-face descriptor (real cast-either-face). The back face is the
        // LAND back face played with no stack; MdfcCastFlow offers the
        // controller a face choice at play time and materializes a fresh
        // back-face land instance (wired to its ETB "enters tapped"
        // replacement via the supplied ReplacementBus) when chosen. No
        // transform happens.
        var backFace = MdfcFace.Land(
            BackName,
            (landOwner, replacements) =>
                SkyclaveBasilicaFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        return card;
    }
}
