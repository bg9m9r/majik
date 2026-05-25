using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Smoke (Limited Edition Alpha, {1}{R}).
///
/// Enchantment. Oracle text:
///   "Players can't untap more than one creature during their untap steps."
///
/// ## Implemented (v1)
/// - Enchantment {1}{R} with owner/controller wiring.
/// - <b>Unconditional untap cap on creatures (CR 502.1)</b>: wired via
///   <see cref="UntapCountCapStaticEffect"/> with <c>MaxCount = 1</c> and
///   a filter restricted to <see cref="CardType.Creature"/>. Unlike
///   Static Orb / Winter Orb, Smoke's cap has no "as long as untapped"
///   rider — the cap is always active while Smoke is on the battlefield
///   (<c>isActive: () =&gt; true</c>). Symmetric — applies to both
///   players' untap steps. On LTB the registration lifts.
///
/// ## Deferred (v1 gaps)
/// - <b>Bot heuristic for "which creature to untap"</b>: same gap as
///   Static Orb / Winter Orb — v1 selection is greedy first-fit on the
///   printed iteration order. A bot heuristic upstream would prefer the
///   best attacker / blocker; the cap surface itself is unchanged.
/// </summary>
[CardName("Smoke")]
public static class SmokeFactory
{
    public const string CardName = "Smoke";
    public const string PrintedManaCost = "{1}{R}";

    /// <summary>
    /// Shape-only constructor — builds Smoke with correct identity. The
    /// untap-cap lifecycle is NOT attached; pass an event bus to the
    /// overload to activate the printed static.
    /// </summary>
    public static Enchantment Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Smoke with optional event-bus wiring. When
    /// <paramref name="eventBus"/> is supplied, the
    /// <see cref="UntapCountCapStaticEffect"/> lifecycle attaches so the
    /// printed "Players can't untap more than one creature during their
    /// untap steps" clause activates on ETB and lifts on LTB.
    /// </summary>
    public static Enchantment Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (eventBus != null)
        {
            new UntapCountCapStaticEffect(
                source: card,
                maxCount: 1,
                // Creatures only — Smoke leaves non-creature permanents
                // to untap freely.
                filter: p => p.HasType(CardType.Creature),
                isActive: () => true,
                eventBus: eventBus).Attach();
        }

        return card;
    }
}
