using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.CardData.MDFCs;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for the FRONT face of the modal double-faced card
/// Akoum Warrior // Akoum Teeth (Zendikar Rising, {5}{R}).
///
/// Creature — Minotaur Warrior 4/5. Oracle text (front, verified against
/// Scryfall):
///   "Trample"
///
/// Back face — <see cref="AkoumTeethFactory"/> (Land — "This land enters
/// tapped." / "{T}: Add {R}.").
///
/// ## MDFC infra (CR 712.3 / 712.4 / 712.6)
///
/// Cast-either-face is modelled by two independent <c>[CardName]</c>-dispatched
/// factories — the same architecture as
/// <see cref="KazanduMammothFactory"/> / <see cref="KazanduValleyFactory"/>
/// (ZNR creature-front + tapland-back MDFC). The front-face card carries a
/// castable <see cref="MdfcFace.Land"/> back-face descriptor on its
/// <see cref="MdfcState"/> so <see cref="Majik.Core.Game.MdfcCastFlow"/> can
/// offer the controller a face choice at play time and materialize a fresh
/// back-face land instance (Akoum Teeth) when chosen. No transform happens —
/// only the chosen face exists (CR 712.4).
///
/// ## Card identity comes from JSON
///
/// Name / type / printed cost / 4/5 P/T are loaded from the embedded JSON
/// definition (<c>akoum-warrior.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory"/>. The <see cref="MdfcState"/> face
/// tracker and the Trample keyword marker are attached in code (the JSON
/// <c>AbilityDefinition</c> schema models neither MDFC faces nor evergreen
/// keywords).
///
/// ## Implemented (v1)
///
/// - 4/5 Creature — Minotaur Warrior, mana cost {5}{R}, owner / controller
///   wired.
/// - <see cref="MdfcState"/> attached (front = "Akoum Warrior", back =
///   "Akoum Teeth") with a castable <see cref="MdfcFace.Land"/> back face;
///   starts on the front face.
/// - <b>Trample</b> (CR 702.19) as a <see cref="KeywordAbility"/> marker —
///   the source-of-truth read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> for the
///   excess-damage-to-defending-player combat rule.
///
/// ## References
///
/// - <see cref="KazanduMammothFactory"/> — companion ZNR creature-front MDFC
///   with the same castable-land-back MdfcState shape (minus the landfall
///   trigger; Akoum Warrior's front is a vanilla-with-Trample body).
/// - <see cref="RealitySmasherFactory"/> — vanilla creature whose Trample is
///   added via the same <see cref="KeywordAbility"/>("Trample") marker.
/// </summary>
[CardName("Akoum Warrior")]
public static class AkoumWarriorFactory
{
    public const string CardName = "Akoum Warrior";
    public const string BackName = "Akoum Teeth";
    public const string Slug = "akoum-warrior";
    public const int Power = 4;
    public const int Toughness = 5;

    /// <summary>
    /// Construct Akoum Warrior. Identity (name / Creature / Minotaur Warrior
    /// subtypes / {5}{R} / 4/5) comes from the embedded JSON definition; the
    /// <see cref="MdfcState"/> with the castable land back face and the
    /// Trample keyword marker are layered on in code. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Minotaur Warrior subtypes, {5}{R}, 4/5). The JSON carries no
        // abilities — the MDFC face tracker + Trample marker are layered on.
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
                AkoumTeethFactory.Create(landOwner, replacements));
        card.MdfcState = new MdfcState(CardName, BackName, backFace);

        // CR 702.19 — Trample, as a KeywordAbility marker read by
        // CombatAbilities.HasTrample for the excess-combat-damage rule.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        return card;
    }
}
