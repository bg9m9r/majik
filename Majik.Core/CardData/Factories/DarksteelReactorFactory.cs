using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Counters;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Darksteel Reactor (Darksteel, {4}).
///
/// Artifact. Oracle text:
///   "Indestructible.
///    At the beginning of your upkeep, you may put a charge counter on
///    Darksteel Reactor.
///    At the beginning of your upkeep, if there are twenty or more charge
///    counters on Darksteel Reactor, you win the game."
///
/// (The printed Mirrodin Besieged oracle reads as a single upkeep trigger:
/// "At the beginning of your upkeep, you may put a charge counter on
/// Darksteel Reactor. If Darksteel Reactor has twenty or more charge
/// counters on it, you win the game." v1 wires the two clauses together
/// in a single upkeep trigger body — add-then-check — which is functionally
/// equivalent to the modern split-trigger oracle because both triggers
/// share the same trigger event and resolve in APNAP order.)
///
/// ## Implemented (v1)
/// - Artifact, mana cost {4}, owner/controller wired.
/// - <b>Indestructible</b> (CR 702.12) — wired as a
///   <see cref="KeywordAbility"/> marker. Read by
///   <see cref="Majik.Core.CardData.OracleSpellBinder.MoveToGraveyard"/>'s
///   non-creature destroy gate.
/// - <b>Upkeep trigger</b> (CR 500.4 / 603.1): "At the beginning of your
///   upkeep, you may put a charge counter on Darksteel Reactor. If
///   Darksteel Reactor has twenty or more charge counters on it, you win
///   the game." Wired via <see cref="Triggers.OnStepBegin"/> filtered to
///   <see cref="Majik.Core.StateMachine.PhaseStateType.Upkeep"/> and the
///   controller. Resolution:
///   <list type="number">
///     <item>v1 always adds a charge counter (the "you may" optional gate
///           is deferred — see Deferred section). The counter is placed
///           via <see cref="CounterCollection.Add"/> directly.</item>
///     <item>If the resulting count is &ge; 20 (CR 104.2a — "win the
///           game" effect), mark every supplied opponent as
///           <see cref="Player.MarkLost"/> so the SBA-driven win/loss
///           gates (<see cref="Majik.Core.Game.GameDriver"/>'s
///           alive-count check) resolve the controller as the winner.
///           "You win the game" with no opponents specified is a no-op
///           on the structural path — the wired overload takes the
///           opponent list explicitly.</item>
///   </list>
///
/// ## Win condition modelling
/// Majik does not yet ship a dedicated "win the game" primitive — the
/// engine surfaces the loser side via <see cref="Player.MarkLost"/> /
/// <see cref="Player.HasLost"/>, and <see cref="Majik.Core.Game.GameDriver"/>'s
/// "alive count == 1 → that player wins" rule
/// (<c>GameDriver.cs:169–171</c>) is how the winner is declared. So
/// Reactor's "you win the game" maps to "mark every opponent as lost",
/// which trips the same gate (CR 104.2a — winning the game is observable
/// only via the opponents' loss; multiplayer rules treat each opponent's
/// loss independently and the rules-engine collapses to the same
/// single-survivor check).
///
/// ## Deferred (v1 gaps)
/// - <b>"You may" optional gate</b>: v1 always adds the charge counter
///   when the upkeep trigger resolves. The agent-driven prompt to skip is
///   deferred — same posture as Animation Module's "may pay {1}" gate
///   (no <see cref="Majik.Core.Players.Agents.IPlayerAgent"/> Boolean
///   prompt thread through triggered-ability resolution yet).
/// - <b>ReplacementBus / CounterAddedEvent</b>: the counter is placed
///   directly on the permanent's <see cref="CounterCollection"/> rather
///   than routed through
///   <see cref="Majik.Core.Services.CountersService.Add"/>. Hardened
///   Scales / Doubling Season do not modify charge counters (their
///   replacement effects key on +1/+1 counters specifically), so direct
///   placement is correct for the oracle text; counters-matter triggers
///   that key on Charge specifically (none in the Modern pool today)
///   would miss the event. Wire through CountersService when a card
///   needs it.
/// - <b>Live TriggerManager wiring</b>: the single-arg factory attaches
///   the upkeep trigger to the card but does NOT register it with a
///   <see cref="TriggerManager"/>. Tests fire the trigger manually or
///   invoke the effect directly. The wired overload registers the
///   trigger so bus-driven firing works end-to-end.
/// - <b>"Win the game" reaching steady-state</b>: marking every opponent
///   <see cref="Player.HasLost"/> trips
///   <see cref="Majik.Core.Game.GameDriver"/>'s alive-count gate, which
///   exits the run loop. There is no <c>GameStateMachine</c> transition
///   surface yet for "controller wins" — the winner is implicit in the
///   single-survivor check. CR 104.2a (winning the game) and CR 104.3
///   (losing the game) share this representation.
/// </summary>
[CardName("Darksteel Reactor")]
public static class DarksteelReactorFactory
{
    public const string CardName = "Darksteel Reactor";
    public const string PrintedManaCost = "{4}";

    /// <summary>CR 104.2a — Reactor's printed win threshold.</summary>
    public const int WinThreshold = 20;

    /// <summary>
    /// Construct Darksteel Reactor with no live event-bus / trigger-manager
    /// wiring. The upkeep trigger is attached for shape inspection; tests
    /// fire it by invoking the effect directly. "You win the game" with no
    /// opponents specified is a no-op (the trigger body still adds the
    /// charge counter — the win check just has nobody to mark lost).
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Artifact Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, opponents: null);

    /// <summary>
    /// Construct Darksteel Reactor with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the upkeep trigger is
    /// registered so the bus surfaces it automatically. When
    /// <paramref name="opponents"/> is supplied, reaching the 20-charge
    /// threshold marks every opponent as <see cref="Player.HasLost"/> so
    /// the game's single-survivor gate declares
    /// <paramref name="owner"/> the winner.
    /// </summary>
    public static Artifact Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        IReadOnlyList<Player>? opponents)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var reactor = new Artifact(CardName, PrintedManaCost);
        reactor.SetOwner(owner);
        reactor.SetController(owner);

        // ----------------------------------------------------------------
        // Indestructible (CR 702.12). Marker only — destroy gates read
        // KeywordAbility off Permanent.
        // ----------------------------------------------------------------
        reactor.AddAbility(new KeywordAbility("Indestructible", reactor, owner));

        // ----------------------------------------------------------------
        // Upkeep trigger — CR 603.1 / CR 500.4.
        //   "At the beginning of your upkeep, you may put a charge counter
        //    on Darksteel Reactor. If Darksteel Reactor has twenty or more
        //    charge counters on it, you win the game."
        // v1: always adds the counter (optional "may" deferred), then
        // checks the threshold.
        // ----------------------------------------------------------------
        var upkeepEffect = new Effect(
            "Darksteel Reactor: add a charge counter, win if 20+",
            () =>
            {
                if (reactor.Zone != ZoneType.Battlefield) return;

                // CR 122.1 — direct placement. Charge counters aren't
                // currently modified by any registered replacement effect
                // (Hardened Scales / Doubling Season are +1/+1 specific).
                reactor.Counters.Add(CounterType.Charge);

                // CR 104.2a — winning the game maps to "every opponent
                // loses". GameDriver's alive-count gate then declares the
                // controller the winner. With no opponents supplied (the
                // shape path), the win is unobservable — the threshold
                // check still runs for trigger-shape parity with the
                // wired path.
                var charges = reactor.Counters.Count(CounterType.Charge);
                if (charges >= WinThreshold && opponents != null)
                {
                    foreach (var opp in opponents)
                    {
                        if (opp == null) continue;
                        if (ReferenceEquals(opp, owner)) continue;
                        if (opp.HasLost) continue;
                        opp.MarkLost();
                    }
                }
            });

        var upkeepTrigger = new TriggeredAbility(
            source: reactor,
            controller: owner,
            condition: Triggers.OnStepBegin(
                owner, Majik.Core.StateMachine.PhaseStateType.Upkeep),
            effects: new IEffect[] { upkeepEffect },
            activeZones: new[] { ZoneType.Battlefield });

        reactor.AddAbility(upkeepTrigger);
        triggers?.RegisterTriggeredAbility(upkeepTrigger);

        return reactor;
    }
}
