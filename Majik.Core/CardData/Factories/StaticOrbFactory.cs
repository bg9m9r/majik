using Majik.Core.Cards;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Static Orb (Mirrodin / 7th Edition reprint, {3}).
///
/// Artifact. Oracle text:
///   "As long as Static Orb is untapped, players can't untap more than
///    two permanents during their untap steps."
///
/// ## Implemented (v1)
/// - Artifact {3} with owner/controller wiring.
/// - <b>Conditional untap cap (CR 502.1)</b>: wired via
///   <see cref="UntapCountCapStaticEffect"/> with <c>MaxCount = 2</c> and
///   a permissive filter (every <see cref="Permanent"/> qualifies). The
///   <c>isActive</c> predicate gates on Static Orb's own tap state — when
///   the orb is tapped the cap is dormant, when untapped the cap fires.
///   Re-checked at consultation time so the cap reacts to mid-game tap /
///   untap of Static Orb itself without needing a TapEvent surface
///   (paralleling Howling Mine "as long as it's untapped" wording in the
///   Static Prison family). Symmetric — applies to both players' untap
///   steps. On LTB the registration lifts.
///
/// ## Deferred (v1 gaps)
/// - <b>Bot heuristic for "which two to untap"</b>: v1 selection order is
///   the printed iteration order over the candidate list (the order
///   <see cref="Majik.Core.Zones.ZoneCollection.GetCards"/> returns).
///   A future hook in <see cref="Majik.Core.Game.TurnDriver"/>'s untap
///   step can re-order candidates before the cap pass (prefer mana
///   sources, prefer creatures that can attack, etc.); the cap itself
///   is greedy first-fit on the supplied order.
/// </summary>
[CardName("Static Orb")]
public static class StaticOrbFactory
{
    public const string CardName = "Static Orb";
    public const string PrintedManaCost = "{3}";

    /// <summary>
    /// Shape-only constructor — builds Static Orb with correct identity.
    /// The untap-cap lifecycle is NOT attached; pass an event bus to the
    /// overload to activate the printed static.
    /// </summary>
    public static Artifact Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Construct Static Orb with optional event-bus wiring. When
    /// <paramref name="eventBus"/> is supplied, the
    /// <see cref="UntapCountCapStaticEffect"/> lifecycle attaches so the
    /// printed "As long as Static Orb is untapped, players can't untap
    /// more than two permanents during their untap steps" clause
    /// activates on ETB and lifts on LTB.
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
                maxCount: 2,
                filter: _ => true,
                // "As long as Static Orb is untapped" — re-checked at cap
                // consultation time so mid-game tap/untap of the orb itself
                // toggles the restriction without an event surface.
                isActive: () => !card.IsTapped,
                eventBus: eventBus).Attach();
        }

        return card;
    }
}
