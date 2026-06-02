using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Regress (Mirrodin, {2}{U}).
///
/// Instant. Oracle text:
///   "Return target permanent to its owner's hand."
///
/// The broad bounce — identical effect to <see cref="BoomerangFactory"/> at a
/// different mana cost.
///
/// ## Declarative spell schema (proof of the spell-effect path)
/// <see cref="BuildDefinition"/> declares a single
/// <see cref="ReturnToHandEffectDef"/> verb (filter <c>"permanent"</c>) and
/// routes it through
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the same
/// ability-side <c>return_to_hand</c> verb Boomerang / Karakas use. The target
/// request + CR 608.2b illegal-target fizzle come from the shared
/// <see cref="TargetFilters"/> / <see cref="Majik.Core.Primitives.Fx.BounceToHand"/>
/// primitives.
/// </summary>
[CardName("Regress")]
public static class RegressFactory
{
    public const string CardName = "Regress";
    public const string PrintedManaCost = "{2}{U}";

    /// <summary>
    /// Construct Regress as an Instant card with owner / controller wired.
    /// The resolve SpellDefinition is built on demand via
    /// <see cref="BuildDefinition"/> at the SpellCastFlow resolver wire-up site.
    /// </summary>
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
    /// declaratively (the <c>return_to_hand</c> verb on the <c>permanent</c>
    /// target filter).
    /// </summary>
    /// <param name="zoneService">Accepted for call-site compatibility; the
    /// declarative bounce verb resolves through
    /// <see cref="Majik.Core.Primitives.Fx.BounceToHand(ICard, ZoneService?)"/>
    /// using raw zone moves.</param>
    public static SpellDefinition BuildDefinition(ZoneService? zoneService = null) =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new ReturnToHandEffectDef { TargetFilter = "permanent" },
            });
}
