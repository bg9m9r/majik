using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Boon Reflection (Tenth Edition, {4}{W}).
///
/// Enchantment. Oracle text:
///   "If you would gain life, you gain twice that much life instead."
///
/// ## Implemented (v1)
/// - Card identity (Enchantment, mana cost {4}{W}, owner / controller wiring).
/// - <b>Asymmetric life-gain doubling</b> (CR 614 / CR 119.6) — single
///   <see cref="LifeGainIntent"/> replacement registered on the controller's
///   attached <see cref="ReplacementBus"/>. Every life-gain intent whose
///   <see cref="LifeGainIntent.Target"/> is the controller is rewritten to
///   twice the original <see cref="LifeGainIntent.Amount"/>. Gated on Boon
///   Reflection being on the battlefield (registration lifetime is the
///   permanent's battlefield stint; the predicate short-circuits while
///   off-battlefield).
/// - Per-effect dedup in <see cref="ReplacementBus.Apply{TIntent}"/>
///   (CR 616.1c) lets the clause stack: two copies of Boon Reflection
///   quadruple life gain; Boon Reflection + Beacon of Immortality's
///   doubling on a specific gain stack via the standard 614-order
///   choose-which-replacement-first prompt (deferred — v1 applies in
///   registration order).
///
/// ## Notes
/// - Mirrors the shape of <see cref="FurnaceOfRathFactory"/> /
///   <see cref="DamageDoubleReplacement"/> but for life gain. No
///   bespoke "LifeGainDoubleReplacement" primitive — the
///   <see cref="LambdaReplacement{TIntent}"/> closure is small enough
///   to live inline (same posture as
///   <see cref="RoilingVortexFactory"/>'s zero-out lambda).
/// - <b>Two-overload shape</b>: single-arg <see cref="Create(Player)"/>
///   is shape-only for dispatcher tests (no bus → no replacement
///   registration); the <see cref="Create(Player, ReplacementBus?)"/>
///   overload wires the live doubling clause when the controller's bus
///   is supplied.
/// - Player + bus discovery is keyed off the controller's
///   <see cref="Player.Replacements"/> in normal play; the explicit
///   bus parameter here keeps the factory unit-testable without a full
///   Game wiring.
/// </summary>
[CardName("Boon Reflection")]
public static class BoonReflectionFactory
{
    public const string CardName = "Boon Reflection";
    public const string Cost = "{4}{W}";

    /// <summary>
    /// Construct Boon Reflection with card identity only — no life-gain
    /// replacement is registered. Suitable for shape / dispatcher tests;
    /// the bus-driven doubling lives on the
    /// <see cref="Create(Player, ReplacementBus?)"/> overload.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, replacements: null);

    /// <summary>
    /// Construct Boon Reflection. When <paramref name="replacements"/> is
    /// supplied, the asymmetric "double every life-gain intent targeting
    /// the controller" CR 614 replacement is registered against it, gated
    /// on Boon Reflection being on the battlefield. Without a bus only
    /// the structural shape is wired.
    /// </summary>
    public static Enchantment Create(Player owner, ReplacementBus? replacements)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, Cost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Asymmetric life-gain doubling (CR 614 / CR 119.6). Predicate
        // matches the controller as the gain target AND requires Boon
        // Reflection to still be on the battlefield.
        // ----------------------------------------------------------------
        if (replacements != null)
        {
            replacements.Register<LifeGainIntent>(new LambdaReplacement<LifeGainIntent>(
                applies: (intent, _) =>
                    intent.Amount > 0
                    && ReferenceEquals(intent.Target, card.Controller)
                    && card.Zone == ZoneType.Battlefield,
                replace: (intent, _) => intent with { Amount = intent.Amount * 2 },
                oneShot: false,
                tag: card));
        }

        return card;
    }
}
