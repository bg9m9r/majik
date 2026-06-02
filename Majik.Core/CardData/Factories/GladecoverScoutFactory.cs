using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Gladecover Scout (Innistrad, {G}).
///
/// Creature — Elf Scout 1/1. Oracle text (verified against Scryfall):
///   "Hexproof (This creature can't be the target of spells or abilities
///    your opponents control.)"
///
/// The card's base shape (name, Creature, Elf/Scout subtypes, {G}, 1/1) is
/// materialised from the embedded JSON definition
/// (<c>gladecover-scout.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed static
/// rider (Hexproof) is layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers, so it
/// lives in the factory (same posture as the analogue
/// <see cref="SylvanCaryatidFactory"/> / <see cref="InvisibleStalkerFactory"/>).
///
/// ## Implemented (v1)
/// - 1/1 <see cref="Creature"/> — Elf Scout at {G}.
/// - <b>Hexproof (CR 702.11)</b> — wired as an UNCONDITIONAL
///   <see cref="KeywordAbility"/> marker. This is the live read path:
///   <see cref="Majik.Core.Targeting.TargetLegality"/> consults the
///   "Hexproof" keyword to reject targeting by spells / abilities an
///   opponent controls (CR 702.11b). Gladecover Scout's hexproof is always
///   on, so the plain keyword marker is correct — same wiring shape as
///   <see cref="SylvanCaryatidFactory"/>.
///
/// Gladecover Scout has no activated, triggered, or mana abilities and no
/// static effects beyond the Hexproof keyword marker.
/// </summary>
[CardName("Gladecover Scout")]
public static class GladecoverScoutFactory
{
    public const string CardName = "Gladecover Scout";
    public const string Slug = "gladecover-scout";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Gladecover Scout: identity from the JSON definition plus the
    /// Hexproof keyword marker wired unconditionally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature,
        // Elf/Scout subtypes, {G}, 1/1). The JSON carries no abilities —
        // Hexproof is layered on below.
        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.11 — Hexproof (unconditional). KeywordAbility marker read by
        // TargetLegality to deny opponent-controlled spells/abilities from
        // targeting this creature (CR 702.11b).
        card.AddAbility(new KeywordAbility("Hexproof", card, owner));

        return card;
    }
}
