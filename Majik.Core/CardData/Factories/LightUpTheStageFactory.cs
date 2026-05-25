using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Light Up the Stage (Ravnica Allegiance, {2}{R}).
///
/// Sorcery. Oracle text:
///   "Spectacle {R} (You may cast this spell for its spectacle cost rather
///    than its mana cost if an opponent lost life this turn.)
///    Exile the top three cards of your library. Until the end of your
///    next turn, you may play those cards."
///
/// ## Implemented (v1)
/// - Sorcery shape, printed mana cost {2}{R}.
/// - <b>Resolve</b> (via <see cref="BuildResolveEffect"/>): exile the top
///   three cards of the caster's library to the caster's exile zone (CR
///   701.20). Then stamp a runtime exile-cast grant
///   (<see cref="Card.GrantRuntimeExileCast"/>) on each exiled card so the
///   caster may cast them from exile via
///   <see cref="ExileCastAlternativeCost"/>. The granted cost is each
///   card's printed mana cost — "you may play those cards" with no
///   alternative cost rider (CR 118.9 / Ravnica Allegiance reminder text).
/// - <b>"Until the end of your next turn" cleanup</b>: when an
///   <see cref="IEventBus"/> is supplied, the resolve body subscribes a
///   <see cref="StepStartedEvent"/> handler that counts Cleanup steps
///   belonging to the caster (CR 514.2). The grant clears on the SECOND
///   such Cleanup (= the cleanup of the caster's NEXT turn). The first
///   Cleanup belongs to the caster's CURRENT turn (sorcery → cast on
///   own turn → "your next turn" = the turn after this one). Without a
///   bus the grant persists until callers clear it manually (test path).
/// - <b>Spectacle {R}</b> alternative cost (CR 702.118) is exposed via
///   <see cref="BuildSpectacleCost"/>, which routes through
///   <see cref="SpectacleBinder.TryBind"/> against the printed
///   <see cref="OracleText"/>. The returned
///   <see cref="SpectacleAlternativeCost"/> (or null when no opponent has
///   lost life this turn) is passed by callers to
///   <see cref="Majik.Core.Game.SpellCastFlow.CastAsync"/>; the existing
///   alt-cost path handles cost substitution. Going through the binder
///   keeps the named-factory and data-driven oracle paths in sync —
///   mirrors <see cref="FaithlessLootingFactory.BuildFlashbackCost"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Empty-library mid-exile</b>: v1 stops exiling when the library
///   runs out. There is no "tried to draw from empty library" flag for
///   the exile move (CR 701.20 doesn't impose one), so an empty library
///   mid-resolve simply yields fewer than three grants.
/// - <b>"May play those cards" includes lands</b>: the Spectacle reminder
///   says "play", which covers both casting and playing as a land
///   (CR 305.2). The runtime exile-cast grant authorises casting; lands
///   would need a parallel "play this land from exile" grant. None of the
///   top-200 Burn shells we care about benefit from this corner-case so
///   v1 ships the spell-only authorisation.
/// - <b>Multi-turn "until end of your next turn" precision</b>: v1 counts
///   Cleanup steps owned by the caster (CR 514.2) — the first cleanup
///   belongs to this turn, the second to the caster's next turn. This is
///   sound in two-player games where turns alternate; in multiplayer the
///   accounting is still correct because StepStartedEvent.Player is the
///   active player at the start of that cleanup.
/// </summary>
[CardName("Light Up the Stage")]
public static class LightUpTheStageFactory
{
    public const string CardName = "Light Up the Stage";
    public const string PrintedManaCost = "{2}{R}";
    public const int CardsExiled = 3;

    /// <summary>
    /// Oracle text used by <see cref="BuildSpectacleCost"/> to derive the
    /// spectacle cost via <see cref="SpectacleBinder.TryBind"/>. Kept on
    /// the factory so the production load path (Scryfall row → oracle text
    /// → binder) and the named-factory test path bind the same shape.
    /// </summary>
    public const string OracleText =
        "Spectacle {R} (You may cast this spell for its spectacle cost rather than its mana cost if an opponent lost life this turn.)\n"
        + "Exile the top three cards of your library. Until the end of your next turn, you may play those cards.";

    /// <summary>
    /// Construct Light Up the Stage with no live event-bus wiring. The
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
    /// Build Light Up the Stage's resolve effect — exile top 3 cards of
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
                "Light Up the Stage: exile top 3 + may-cast-from-exile until end of your next turn",
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
                            // Only clear the grant we set — if a different
                            // effect re-stamped meanwhile we still revoke
                            // (CR 514.2 — duration ends regardless of who
                            // set the latest stamp). Mirrors Snapcaster /
                            // Ragavan EOT clear.
                            s.ClearRuntimeExileCast();
                        }
                        if (handler != null) eventBus.Unsubscribe(handler);
                    };
                    eventBus.Subscribe(handler);
                }),
        };
    }

    /// <summary>
    /// Build the Spectacle {R} alternative cost (CR 702.118) by routing
    /// <see cref="OracleText"/> through <see cref="SpectacleBinder.TryBind"/>.
    /// Returns <c>null</c> when no opponent has lost life this turn — the
    /// caller falls back to the printed mana cost. Going through the
    /// binder keeps named-factory and data-driven oracle paths in sync
    /// — mirrors <see cref="FaithlessLootingFactory.BuildFlashbackCost"/>.
    /// </summary>
    public static SpectacleAlternativeCost? BuildSpectacleCost(
        Player caster,
        IReadOnlyList<Player> allPlayers)
    {
        return SpectacleBinder.TryBind(OracleText, caster, allPlayers);
    }
}
