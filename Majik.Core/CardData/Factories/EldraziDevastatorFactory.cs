using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Eldrazi Devastator (Battle for Zendikar, {8}).
///
/// Creature — Eldrazi 8/9. Oracle text (verified against Scryfall 2026-06-02):
///   "Trample"
///
/// A bulk colorless beater — the cheap, vanilla-with-Trample end of the
/// Eldrazi body spectrum. Same shape as <see cref="UlamogsCrusherFactory"/>
/// (large {8} Eldrazi creature) but with the Annihilator + must-attack riders
/// stripped: Eldrazi Devastator's only printed ability is Trample.
///
/// ## Card identity comes from JSON
///
/// Name / Creature — Eldrazi / printed {8} cost (colorless, derived from the
/// generic mana cost) / 8/9 P/T are materialised from the embedded JSON
/// definition (<c>eldrazi-devastator.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON
/// <c>AbilityDefinition</c> schema models no evergreen keywords, so the
/// Trample marker is layered on in code — the same posture as
/// <see cref="AkoumWarriorFactory"/> / <see cref="RealitySmasherFactory"/>.
///
/// ## Implemented (v1)
///
/// - 8/9 Creature — Eldrazi, mana cost {8}, owner / controller wired.
/// - <b>Trample</b> (CR 702.19) as a <see cref="KeywordAbility"/> marker —
///   the source-of-truth read by
///   <see cref="Majik.Core.Combat.CombatAbilities.HasTrample"/> for the
///   excess-combat-damage-to-defending-player rule.
/// </summary>
[CardName("Eldrazi Devastator")]
public static class EldraziDevastatorFactory
{
    public const string CardName = "Eldrazi Devastator";
    public const string Slug = "eldrazi-devastator";
    public const int Power = 8;
    public const int Toughness = 9;

    /// <summary>
    /// Construct Eldrazi Devastator. Identity (name / Creature / Eldrazi
    /// subtype / {8} / 8/9) comes from the embedded JSON definition; the
    /// Trample keyword marker is layered on in code. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Eldrazi subtype, {8}, 8/9). The JSON carries no abilities — the
        // Trample marker is layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // CR 702.19 — Trample, as a KeywordAbility marker read by
        // CombatAbilities.HasTrample for the excess-combat-damage rule.
        card.AddAbility(new KeywordAbility("Trample", card, owner));

        return card;
    }
}
