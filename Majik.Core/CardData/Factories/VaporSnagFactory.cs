using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Game;
using Majik.Core.Players;
using Majik.Core.Services;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Vapor Snag (New Phyrexia, {U}).
///
/// Instant. Oracle text:
///   "Return target creature to its owner's hand.
///    Its controller loses 1 life."
///
/// ## Declarative spell schema (return_to_hand + lose_life_target rider)
/// <see cref="BuildDefinition"/> no longer hand-rolls a bespoke bounce +
/// life-loss closure: it declares a <see cref="ReturnToHandEffectDef"/> over the
/// <c>creature</c> target filter followed by a <see cref="LoseLifeTargetEffectDef"/>
/// rider (<c>Subject = "controller"</c>) that SHARES the bounce's chosen target
/// and drains its controller (CR 119.3) — the same ability-side verbs the engine
/// uses elsewhere, here threaded through
/// <see cref="CardDefRuntime.BuildSpellDefinitionFromEffects"/>.
///
/// CR 608.2g — "its controller" uses last-known information: the rider snapshots
/// the creature's controller before the bounce moves it. CR 608.2b — an illegal
/// target at resolution (the creature already left the battlefield) fizzles BOTH
/// clauses: no bounce AND no life loss. The target request + the bounce / drain
/// resolution come straight from the shared <see cref="TargetFilters"/> /
/// <see cref="Majik.Core.Primitives.Fx.BounceToHand"/> /
/// <see cref="Majik.Core.Primitives.Fx.LoseLife"/> primitives.
/// </summary>
[CardName("Vapor Snag")]
public static class VaporSnagFactory
{
    public const string CardName = "Vapor Snag";
    public const string PrintedManaCost = "{U}";

    /// <summary>
    /// Construct Vapor Snag as an Instant card with owner/controller wired.
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
    /// Build the "return target creature to its owner's hand; its controller
    /// loses 1 life" SpellDefinition declaratively.
    /// </summary>
    /// <param name="zoneService">Accepted for call-site compatibility with the
    /// other spell factories; the declarative bounce verb resolves through
    /// <see cref="Majik.Core.Primitives.Fx.BounceToHand(ICard, ZoneService?)"/>
    /// using raw zone moves.</param>
    public static SpellDefinition BuildDefinition(ZoneService? zoneService = null) =>
        CardDefRuntime.BuildSpellDefinitionFromEffects(
            CardName,
            new EffectDefinition[]
            {
                new ReturnToHandEffectDef { TargetFilter = "creature" },
                new LoseLifeTargetEffectDef { Amount = 1, Subject = "controller" },
            });
}
