using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Just the Wind (Shadows over Innistrad, {1}{U}).
///
/// Instant. Oracle text (Scryfall, verified):
///   "Return target creature to its owner's hand.
///    Madness {U} (If you discard this card, discard it into exile. When you do,
///    cast it for its madness cost or put it into your graveyard.)"
///
/// The plain creature bounce — identical body to <see cref="UnsummonFactory"/>.
///
/// ## Implemented (v1)
/// - Instant shape, mana cost {1}{U}, owner / controller.
/// - <b>Return target creature to its owner's hand</b> —
///   <see cref="BuildDefinition"/> declares a single
///   <see cref="ReturnToHandEffectDef"/>(TargetFilter: "creature") and hands it
///   to <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/> (the same
///   declarative <c>return_to_hand</c> verb Unsummon uses). The target request +
///   CR 608.2b illegal-target fizzle come from the shared declarative filter;
///   the bounce resolves through
///   <see cref="Majik.Core.Primitives.Fx.BounceToHand(ICard, ZoneService?)"/>.
///   In PROD the cast path binds the oracle text via
///   <see cref="OracleSpellBinder"/> (the bounce template).
///
/// ## Madness {U} (CR 702.35) — intrinsic, NOT wired here
/// "Just the Wind" = {U} is catalogued in
/// <see cref="Majik.Core.Keywords.MadnessCatalog"/>; the central discard funnel
/// <see cref="Majik.Core.Primitives.Fx.DiscardCard"/> routes the discarded card
/// to exile + offers it for its madness cost. No factory code needed.
/// </summary>
[CardName("Just the Wind")]
public static class JustTheWindFactory
{
    public const string CardName = "Just the Wind";
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
    /// Build the "return target creature to its owner's hand"
    /// <see cref="SpellDefinition"/> declaratively (the <c>return_to_hand</c>
    /// verb on a <c>creature</c> target filter).
    /// </summary>
    /// <param name="zoneService">Accepted for call-site compatibility with the
    /// other bespoke spell factories; the declarative bounce verb resolves
    /// through <see cref="Majik.Core.Primitives.Fx.BounceToHand(ICard, ZoneService?)"/>.</param>
    public static SpellDefinition BuildDefinition(ZoneService? zoneService = null) =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new ReturnToHandEffectDef { TargetFilter = "creature" },
            });
}
