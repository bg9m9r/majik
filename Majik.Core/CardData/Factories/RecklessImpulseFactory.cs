using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reckless Impulse (Innistrad: Crimson Vow, {1}{R}).
///
/// Sorcery. Oracle text:
///   "Exile the top two cards of your library. Until the end of your
///    next turn, you may play those cards."
///
/// ## Implemented (v1)
/// - Sorcery shape, printed mana cost {1}{R}, MV 2.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/>): exile the top
///   two cards of the caster's library to the caster's exile zone (CR 701.20).
///   Then stamp a runtime exile-cast grant (<see cref="Card.GrantRuntimeExileCast"/>)
///   on each exiled card so the caster may cast them from exile via
///   <see cref="ExileCastAlternativeCost"/>. The granted cost is each
///   card's printed mana cost — "you may play those cards" with no
///   alternative cost rider (CR 118.9).
/// - <b>"Until the end of your next turn" cleanup</b>: when an
///   <see cref="IEventBus"/> is supplied, the resolve body subscribes a
///   <see cref="StepStartedEvent"/> handler that counts Cleanup steps
///   belonging to the caster (CR 514.2). The grant clears on the SECOND
///   such Cleanup (= the cleanup of the caster's NEXT turn). The first
///   Cleanup belongs to the caster's CURRENT turn (sorcery → cast on
///   own turn → "your next turn" = the turn after this one).
///
/// ## Deferred (v1 gaps)
/// - <b>Empty-library mid-exile</b>: v1 stops exiling when the library
///   runs out (CR 701.20 does not impose an SBA flag for exile).
/// - <b>"May play those cards" includes lands</b>: the grant authorises
///   casting; lands would need a separate "play this land from exile" grant.
///   v1 ships the spell-only authorisation matching the engine pattern used
///   by LightUpTheStageFactory.
/// - <b>Multi-turn next-turn precision</b>: v1 counts Cleanup steps owned
///   by the caster (CR 514.2) — sound in two-player; correct in multiplayer
///   because StepStartedEvent.Player is the active player at cleanup start.
/// </summary>
[CardName("Reckless Impulse")]
public static class RecklessImpulseFactory
{
    public const string CardName = "Reckless Impulse";
    public const string PrintedManaCost = "{1}{R}";
    public const int CardsExiled = 2;

    /// <summary>
    /// Construct Reckless Impulse with no live event-bus wiring. The
    /// runtime exile-cast grant set by <see cref="BuildResolveEffect"/>
    /// will be stamped on the exiled cards but will NOT be cleared
    /// automatically (no Cleanup subscription). Callers exercising the
    /// effect in tests must clear via <see cref="Card.ClearRuntimeExileCast"/>
    /// to model "until end of your next turn" by hand.
    /// </summary>
    public static Sorcery Create(Player owner)
    {
        ArgumentNullException.ThrowIfNull(owner);
        var card = new Sorcery(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);
        return card;
    }

    /// <summary>
    /// Build Reckless Impulse's resolve effect — exile top 2 cards of
    /// <paramref name="caster"/>'s library and stamp a runtime exile-cast
    /// grant on each so the caster may cast them from exile for their
    /// printed mana cost. When <paramref name="eventBus"/> is non-null,
    /// schedule the EOT cleanup on the caster's NEXT turn's Cleanup step
    /// (CR 514.2 — second cleanup belonging to the caster after the cast).
    /// </summary>
    public static IReadOnlyList<IEffect> BuildResolveEffect(
        Player caster,
        IEventBus? eventBus = null)
    {
        ArgumentNullException.ThrowIfNull(caster);

        return new IEffect[]
        {
            new Effect(
                "Reckless Impulse: exile top 2 + may-cast-from-exile until end of your next turn",
                () =>
                {
                    var stamped = new List<Card>(CardsExiled);
                    for (var i = 0; i < CardsExiled; i++)
                    {
                        var top = caster.Zones.Library.GetCards().FirstOrDefault();
                        if (top == null) break; // library underflow — no SBA flag for exile

                        caster.Zones.Library.RemoveCard(top);
                        caster.Zones.Exile.AddCard(top);
                        top.SetZone(ZoneType.Exile);

                        if (top is Card concrete)
                        {
                            // CR 118.9 — grant matches ExileCastAlternativeCost.
                            // Cost = printed mana cost ("you may play those
                            // cards" with no alternate-cost rider).
                            concrete.GrantRuntimeExileCast(caster, concrete.ManaCostValue);
                            stamped.Add(concrete);
                        }
                    }

                    if (stamped.Count == 0 || eventBus == null) return;

                    // CR 514.2 — schedule "until end of your next turn"
                    // cleanup. Count Cleanup steps where the active player
                    // is the caster. The cast resolves during caster's
                    // turn (sorcery, CR 307.1) → first such Cleanup is
                    // THIS turn's cleanup (grant must survive) and the
                    // second is the caster's NEXT turn's cleanup (clear).
                    var cleanupsSeen = 0;
                    Action<StepStartedEvent>? handler = null;
                    handler = (e) =>
                    {
                        if (e.StepType != PhaseStateType.Cleanup) return;
                        if (!ReferenceEquals(e.Player, caster)) return;
                        cleanupsSeen++;
                        if (cleanupsSeen < 2) return;

                        foreach (var s in stamped)
                        {
                            // Only clear the grant we set — mirrors the
                            // Snapcaster / LightUpTheStage EOT clear pattern.
                            s.ClearRuntimeExileCast();
                        }
                        if (handler != null) eventBus.Unsubscribe(handler);
                    };
                    eventBus.Subscribe(handler);
                }),
        };
    }
}
