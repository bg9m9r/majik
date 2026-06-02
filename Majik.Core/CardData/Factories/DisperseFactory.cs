using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Disperse ({1}{U}).
///
/// Instant. Oracle text:
///   "Return target nonland permanent to its owner's hand."
///
/// The nonland bounce — targets any permanent that is not a land
/// (creatures, artifacts, enchantments, planeswalkers). Compare
/// <see cref="BoomerangFactory"/> which hits any permanent including lands,
/// and <see cref="UnsummonFactory"/> which is restricted to creatures.
///
/// ## Declarative spell schema (proof of the spell-effect path)
/// <see cref="BuildDefinition"/> declares a single
/// <see cref="ReturnToHandEffectDef"/> verb (filter <c>"nonland_permanent"</c>)
/// and routes it through
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> — the same
/// ability-side <c>return_to_hand</c> verb Karakas / Unsummon / Boomerang use.
/// The nonland restriction is enforced both at gather time and (CR 608.2b) at
/// resolution via the shared <see cref="TargetFilters"/> predicate, so a land
/// passed as a raw target fizzles cleanly.
/// </summary>
[CardName("Disperse")]
public static class DisperseFactory
{
    public const string CardName = "Disperse";
    public const string PrintedManaCost = "{1}{U}";

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "return target nonland permanent to its owner's hand"
    /// SpellDefinition declaratively (the <c>return_to_hand</c> verb on the
    /// <c>nonland_permanent</c> target filter).
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
                new ReturnToHandEffectDef { TargetFilter = "nonland_permanent" },
            });
}
