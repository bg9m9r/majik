using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Slippery Bogle (Eventide / Modern Horizons, {G/U}).
///
/// Creature — Beast 1/1. Oracle text (verified against Scryfall):
///   "Hexproof (This creature can't be the target of spells or abilities
///    your opponents control.)"
///
/// A near-vanilla hexproof one-drop — the {G/U} hybrid cost, Creature/Beast
/// types and 1/1 body are materialised from the embedded JSON definition
/// (<c>slippery-bogle.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The single printed static
/// rider (Hexproof) is layered on top here — the JSON
/// <c>AbilityDefinition</c> schema doesn't express keyword markers, so it
/// lives in the factory (same posture as <see cref="SylvanCaryatidFactory"/>
/// and the analogue <see cref="InvisibleStalkerFactory"/> minus the
/// unblockable rider).
///
/// ## Implemented (v1)
/// - <b>Creature — Beast {G/U} 1/1</b>, owner/controller wired.
/// - <b>Hexproof (CR 702.11)</b> — wired as an UNCONDITIONAL
///   <see cref="KeywordAbility"/> marker. This is the live read path:
///   <see cref="Majik.Core.Targeting.TargetLegality"/> consults the
///   "Hexproof" keyword directly to reject targeting by spells / abilities
///   an opponent controls (CR 702.11b), regardless of tap state. Same wiring
///   shape as <see cref="SylvanCaryatidFactory"/> /
///   <see cref="StripedRiverwinderFactory"/>.
///
/// Slippery Bogle has no activated, triggered, or other static abilities.
/// </summary>
[CardName("Slippery Bogle")]
public static class SlipperyBogleFactory
{
    public const string CardName = "Slippery Bogle";
    public const string Slug = "slippery-bogle";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Build Slippery Bogle: identity from the JSON definition plus the
    /// Hexproof keyword marker wired unconditionally.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.11 — Hexproof (unconditional). KeywordAbility marker read by
        // the targeting validator (TargetLegality) to deny opponent-controlled
        // spells/abilities from selecting this creature as a target.
        card.AddAbility(new KeywordAbility("Hexproof", card, owner));

        return card;
    }
}
