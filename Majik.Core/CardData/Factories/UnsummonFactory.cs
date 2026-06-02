using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Unsummon ({U}).
///
/// Instant. Oracle text:
///   "Return target creature to its owner's hand."
///
/// The plain bounce — <see cref="VaporSnagFactory"/> without the "its
/// controller loses 1 life" rider. CR 608.2b: if the chosen target is no
/// longer a creature on the battlefield at resolution, the effect does
/// nothing.
///
/// ## Declarative spell schema (proof of the spell-effect path)
/// <see cref="BuildDefinition"/> no longer hand-rolls a bespoke bounce
/// closure: it declares a single <see cref="ReturnToHandEffectDef"/> verb and
/// hands it to <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> —
/// the same ability-side <c>return_to_hand</c> verb Karakas uses, now reused on
/// the instant/sorcery cast path. The target request + CR 608.2b illegal-target
/// fizzle come straight from the shared <see cref="TargetFilters"/> /
/// <see cref="Majik.Core.Primitives.Fx.BounceToHand"/> primitives.
/// </summary>
[CardName("Unsummon")]
public static class UnsummonFactory
{
    public const string CardName = "Unsummon";
    public const string PrintedManaCost = "{U}";

    public static Instant Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Instant(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build the "return target creature to its owner's hand" SpellDefinition
    /// declaratively (the <c>return_to_hand</c> verb on a <c>creature</c>
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
                new ReturnToHandEffectDef { TargetFilter = "creature" },
            });
}
