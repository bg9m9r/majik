using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Winter Orb (Limited Edition Alpha, {2}).
///
/// Artifact. Oracle text:
///   "As long as Winter Orb is untapped, players can't untap more than
///    one land during their untap steps."
///
/// ## Implemented (v1)
/// - Artifact {2} with owner/controller wiring.
/// - <b>Conditional untap cap on lands (CR 502.1)</b>: wired via
///   <see cref="UntapCountCapStaticEffect"/> with <c>MaxCount = 1</c> and
///   a filter restricted to <see cref="CardType.Land"/>. Same conditional
///   shape as <see cref="StaticOrbFactory"/> — the <c>isActive</c>
///   predicate gates on Winter Orb's own tap state so the cap only fires
///   while the orb is untapped. Re-checked at consultation time.
///   Symmetric — applies to both players' untap steps. On LTB the
///   registration lifts.
///
/// ## Deferred (v1 gaps)
/// - <b>Bot heuristic for "which land to untap"</b>: same gap as Static
///   Orb — v1 selection is greedy first-fit on the printed iteration
///   order. A bot heuristic upstream of the cap pass would prefer the
///   "best" land (typically a colored / utility land over a basic) but
///   doesn't change the cap surface itself.
/// </summary>
[CardName("Winter Orb")]
public static class WinterOrbFactory
{
    public const string CardName = "Winter Orb";
    public const string PrintedManaCost = "{2}";

    /// <summary>
    /// Shape-only constructor — builds Winter Orb with correct identity.
    /// The untap-cap lifecycle is NOT attached; pass an event bus to the
    /// overload to activate the printed static.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Winter Orb with optional event-bus wiring. When
    /// <paramref name="eventBus"/> is supplied, the
    /// <see cref="UntapCountCapStaticEffect"/> lifecycle attaches so the
    /// printed "As long as Winter Orb is untapped, players can't untap
    /// more than one land during their untap steps" clause activates on
    /// ETB and lifts on LTB.
    /// </summary>
    public static Artifact Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Artifact(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        if (eventBus != null)
        {
            new UntapCountCapStaticEffect(
                source: card,
                maxCount: 1,
                // Lands only — Winter Orb leaves non-land permanents to
                // untap freely.
                filter: p => p.HasType(CardType.Land),
                // "As long as Winter Orb is untapped" — re-checked at cap
                // consultation time so mid-game tap/untap of the orb itself
                // toggles the restriction.
                isActive: () => !card.IsTapped,
                eventBus: eventBus).Attach();
        }

        return card;
    }
}
