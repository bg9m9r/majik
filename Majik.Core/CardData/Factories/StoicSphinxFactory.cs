using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stoic Sphinx (Modern Horizons 3, {2}{U}{U}).
///
/// Creature — Sphinx 5/3. Oracle text (verified against Scryfall 2026-06-24):
///   "Flash
///    Flying
///    This creature has hexproof as long as you haven't cast a spell this turn."
///
/// ## Shape source
/// Card identity (name, {2}{U}{U}, 5/3, Creature — Sphinx, Flash + Flying) is
/// loaded from <c>Majik.Core/CardData/Cards/stoic-sphinx.json</c> via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> and built through
/// <see cref="CardDefinitionFactory"/>. The <c>keywords</c> array carries Flash
/// (CR 702.8) and Flying (CR 702.9) as <see cref="Abilities.KeywordAbility"/>
/// markers — the cast-flow consults Flash for instant-speed casting and
/// <see cref="Majik.Core.Combat.CombatAbilities.HasFlying"/> /
/// <c>CanBlockFlying</c> surface the evasion / block-legality from Flying. The
/// conditional-hexproof static is attached in code below.
///
/// ## Implemented (v1)
/// - <b>5/3 Creature — Sphinx at {2}{U}{U} with Flash + Flying</b> (from JSON).
/// - <b>Conditional hexproof (CR 702.11)</b> — "This creature has hexproof as
///   long as you haven't cast a spell this turn." Wired via
///   <see cref="HexproofWhileYouHaventCastSpellEffect"/>, a Layer-6 self-applied
///   continuous effect (CR 613.3) that adds the Hexproof keyword to the Sphinx's
///   computed characteristics only while its controller hasn't cast a spell this
///   turn. The direct sibling of <see cref="HexproofWhileUntappedEffect"/>
///   (Paradise Druid), swapping the "untapped" gate for a "you haven't cast a
///   spell this turn" gate. The targeting validator
///   (<see cref="Majik.Core.Targeting.TargetLegality"/>) reads
///   <c>ActiveEffects.Compute(c).Keywords</c>, so the Sphinx is untargetable by
///   opponents while its controller has cast no spells this turn, and a legal
///   target once they have.
///
///   The effect tracks the "you've cast a spell this turn" condition off the
///   live event bus (<see cref="ContinuousEffectsService.EventBus"/>): a
///   <see cref="Domain.DomainEvents.SpellCastEvent"/> whose spell's controller
///   is the Sphinx's controller drops hexproof for the rest of the turn, and a
///   <see cref="Events.TurnStartedEvent"/> restores it at the next turn boundary
///   (CR 500.1 / 514). The effect is registered only on the
///   <see cref="Create(Player, ContinuousEffectsService)"/> overload (the
///   service must also be assigned to <see cref="Creature.ActiveEffects"/> so
///   the targeting read sees it). The single-arg dispatcher path
///   (<see cref="Create(Player)"/>) builds the card shape (incl. Flash + Flying)
///   without registering the effect — suitable for identity / dispatcher tests.
///   A printed Hexproof <see cref="Abilities.KeywordAbility"/> marker is
///   deliberately NOT attached: that would make the Sphinx hexproof
///   unconditionally (the validator's keyword-fallback path), which would be
///   incorrect once its controller has cast a spell.
/// </summary>
[CardName("Stoic Sphinx")]
public static class StoicSphinxFactory
{
    public const string CardName = "Stoic Sphinx";

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource("stoic-sphinx");

    /// <summary>
    /// Build Stoic Sphinx's card shape (types, Sphinx subtype, {2}{U}{U}, 5/3,
    /// Flash + Flying keywords). The conditional-hexproof continuous effect is
    /// NOT registered on this path — use
    /// <see cref="Create(Player, ContinuousEffectsService)"/> to wire it into a
    /// live continuous-effects service. Suitable for identity / dispatcher
    /// tests. This is the overload <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Stoic Sphinx and wire its conditional-hexproof continuous effect
    /// (CR 702.11 / 613.3) into <paramref name="effects"/>. The service is also
    /// assigned to <see cref="Creature.ActiveEffects"/> so the targeting
    /// validator reads the computed keyword set, granting hexproof only while
    /// the controller hasn't cast a spell this turn. The effect tracks the
    /// condition off the service's <see cref="ContinuousEffectsService.EventBus"/>.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService effects)
    {
        ArgumentNullException.ThrowIfNull(effects);

        var card = Create(owner);
        card.ActiveEffects = effects;
        effects.Register(new HexproofWhileYouHaventCastSpellEffect(card, effects.EventBus));
        return card;
    }
}
