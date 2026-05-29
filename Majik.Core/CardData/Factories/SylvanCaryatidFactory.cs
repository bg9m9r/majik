using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Sylvan Caryatid (Theros, {1}{G}).
///
/// Creature — Plant 0/3. Oracle text (Scryfall):
///   "Defender, hexproof
///    {T}: Add one mana of any color."
///
/// ## Implemented (v1)
/// - <b>Creature — Plant {1}{G} 0/3</b>, owner/controller wired. Types,
///   subtype, P/T and mana cost come from
///   <c>Majik.Core/CardData/Cards/sylvan-caryatid.json</c> built by
///   <see cref="CardDefinitionFactory"/> — same thin-wrapper shape as
///   Paradise Druid.
/// - <b>"Add one mana of any color"</b> (CR 605.1) — modeled as five
///   <see cref="ManaAbility"/> instances (one per WUBRG) in the JSON,
///   mirroring the Paradise Druid / Birds of Paradise any-colour pattern.
///   Each taps the Caryatid; the mana picker can satisfy any single colour
///   pip via this creature.
/// - <b>Defender</b> (CR 702.3) — wired as a <see cref="KeywordAbility"/>
///   marker so <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/>
///   surfaces the can't-attack restriction (same shape as Wall of Swords).
/// - <b>Hexproof</b> (CR 702.11) — wired as an UNCONDITIONAL
///   <see cref="KeywordAbility"/> marker. Unlike Paradise Druid
///   ("hexproof as long as it's untapped", a Layer-6 continuous effect),
///   Sylvan Caryatid's hexproof is always on, so the plain keyword marker
///   is correct: <see cref="Majik.Core.Targeting.TargetLegality"/> reads
///   the marker and denies opponent-controlled spells/abilities from
///   selecting it as a target regardless of tap state. Same wiring shape
///   as Striped Riverwinder.
///
/// Sylvan Caryatid has no activated (non-mana) abilities, no triggered
/// abilities, and no static effects beyond the two keyword markers.
/// </summary>
[CardName("Sylvan Caryatid")]
public static class SylvanCaryatidFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("sylvan-caryatid");

    /// <summary>
    /// Build Sylvan Caryatid: identity + five any-colour mana abilities from
    /// the JSON definition, plus the Defender and Hexproof keyword markers
    /// wired unconditionally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.3 — Defender. KeywordAbility marker so
        // CombatAbilities.HasDefender surfaces the can't-attack rider.
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // CR 702.11 — Hexproof (unconditional). KeywordAbility marker read by
        // the targeting validator to deny opponent-controlled spells/abilities
        // from targeting this creature, regardless of tap state.
        card.AddAbility(new KeywordAbility("Hexproof", card, owner));

        return card;
    }
}
