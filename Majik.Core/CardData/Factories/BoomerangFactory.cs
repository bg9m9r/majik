using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boomerang ({U}{U}).
///
/// Instant. Oracle text:
///   "Return target permanent to its owner's hand."
///
/// The broad bounce — returns any permanent (creature, artifact, enchantment,
/// land, or planeswalker). Compare <see cref="UnsummonFactory"/> which is the
/// creature-only variant at {U}. CR 608.2b: if the chosen target is no longer
/// a permanent on the battlefield at resolution, the effect does nothing.
///
/// ## Declarative spell schema (proof of the spell-effect path)
/// <see cref="BuildDefinition"/> declares a single
/// <see cref="ReturnToHandEffectDef"/> verb (filter <c>"permanent"</c>) and
/// routes it through <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>
/// — the same ability-side <c>return_to_hand</c> verb Karakas uses, here reused
/// on the instant cast path with the broadest bounce filter. The target request
/// + CR 608.2b illegal-target fizzle come from the shared
/// <see cref="TargetFilters"/> / <see cref="Majik.Core.Primitives.Fx.BounceToHand"/>
/// primitives.
/// </summary>
[CardName("Boomerang")]
public static class BoomerangFactory
{
    public const string CardName = "Boomerang";
    public const string PrintedManaCost = "{U}{U}";

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "return target permanent to its owner's hand" SpellDefinition
    /// declaratively (the <c>return_to_hand</c> verb on a <c>permanent</c>
    /// target filter).
    /// </summary>
    /// <param name="zoneService">Accepted for call-site compatibility with the
    /// other bespoke spell factories; the declarative bounce verb resolves
    /// through <see cref="Majik.Core.Primitives.Fx.BounceToHand(ICard, ZoneService?)"/>
    /// using raw zone moves (no replacement bus needed for a plain bounce).</param>
    public static SpellDefinition BuildDefinition(ZoneService? zoneService = null) =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new ReturnToHandEffectDef { TargetFilter = "permanent" },
            });
}
