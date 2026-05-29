using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Paradise Druid (Throne of Eldraine, {1}{G}).
///
/// Creature — Elf Druid 2/1. Oracle text (Scryfall):
///   "This creature has hexproof as long as it's untapped. (It can't be the
///    target of spells or abilities your opponents control.)
///    {T}: Add one mana of any color."
///
/// ## Implemented (v1)
/// - <b>Creature — Elf Druid {1}{G} 2/1</b>, owner/controller wired. Types,
///   subtypes, P/T and mana cost come from
///   <c>Majik.Core/CardData/Cards/paradise-druid.json</c> built by
///   <see cref="CardDefinitionFactory"/> — same thin-wrapper shape as
///   Delighted Halfling.
/// - <b>"Add one mana of any color"</b> (CR 605.1) — modeled as five
///   <see cref="Abilities.ManaAbility"/> instances (one per WUBRG) in the
///   JSON, mirroring the Delighted Halfling / Treasure-token any-colour
///   pattern. Each taps the druid; the mana picker can satisfy any single
///   colour pip via this creature.
/// - <b>Conditional hexproof (CR 702.11)</b> — "has hexproof as long as it's
///   untapped." Wired via <see cref="HexproofWhileUntappedEffect"/>, a Layer-6
///   self-applied continuous effect (CR 613.3) that adds the Hexproof keyword
///   to the druid's computed characteristics only while it is untapped. The
///   targeting validator (<see cref="Majik.Core.Targeting.TargetLegality"/>)
///   reads <c>ActiveEffects.Compute(c).Keywords</c>, so an untapped druid is
///   untargetable by opponents and a tapped druid is a legal target.
///
///   The effect is registered with a live
///   <see cref="ContinuousEffectsService"/> only via the
///   <see cref="Create(Player, ContinuousEffectsService)"/> overload (the
///   service must also be assigned to <see cref="Creature.ActiveEffects"/> so
///   the targeting read sees it). The single-arg dispatcher path
///   (<see cref="Create(Player)"/>) builds the card shape without registering
///   the effect — suitable for identity / mana-ability / dispatcher tests.
///   A printed Hexproof <see cref="Abilities.KeywordAbility"/> marker is
///   deliberately NOT attached: that would make the druid hexproof
///   unconditionally (the validator's keyword-fallback path), which would be
///   incorrect for the tapped state.
/// </summary>
[CardName("Paradise Druid")]
public static class ParadiseDruidFactory
{
    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("paradise-druid");

    /// <summary>
    /// Build Paradise Druid's card shape (types, subtypes, P/T, five
    /// any-colour mana abilities). The conditional-hexproof continuous effect
    /// is NOT registered on this path — use
    /// <see cref="Create(Player, ContinuousEffectsService)"/> to wire it into
    /// a live continuous-effects service. Suitable for identity / mana /
    /// dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        (Creature)CardDefinitionFactory.Build(Definition, owner);

    /// <summary>
    /// Build Paradise Druid and wire its conditional-hexproof continuous
    /// effect (CR 702.11 / 613.3) into <paramref name="effects"/>. The service
    /// is also assigned to <see cref="Creature.ActiveEffects"/> so the
    /// targeting validator reads the computed keyword set, granting hexproof
    /// only while the druid is untapped.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        var card = Create(owner);
        card.ActiveEffects = effects;
        effects.Register(new HexproofWhileUntappedEffect(card));
        return card;
    }
}
