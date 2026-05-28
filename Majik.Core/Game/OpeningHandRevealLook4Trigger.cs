using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// CR 103.5 / CR 603.7 — Opening-hand reveal that schedules a "look at the
/// top four cards of your library, put one back on top, exile the rest"
/// trigger on the revealer's first upkeep. Currently scoped to the
/// Devourer-of-Destiny / Conduit-of-Worlds-style reveal clause:
///
///   "You may reveal this card from your opening hand. If you do, at the
///    beginning of your first upkeep, look at the top four cards of your
///    library. You may put one of those cards back on top of your library.
///    Exile the rest."
///
/// Modelled as a sibling of <see cref="OpeningHandLeylineAlternativeCost"/>:
///   - Cards opt in by tagging themselves with
///     <see cref="KeywordAbility"/>(<see cref="RevealKeyword"/>).
///   - Subscriber listens for <see cref="OpeningHandCheckEvent"/>; for each
///     tagged card in the player's hand it asks
///     <see cref="IPlayerAgent.ChooseYesNoAsync"/> whether to reveal.
///   - On a yes-answer a <see cref="DelayedTriggeredAbility"/> is registered
///     with the supplied <see cref="TriggerManager"/>. Its condition is a
///     <see cref="StepStartedEvent"/> filter that fires the first time
///     <see cref="PhaseStateType.Upkeep"/> begins on the revealer's turn —
///     then auto-unregisters via TriggerManager's delayed-ability sweep
///     (Rule 603.7d).
///
/// ## CR citations
///   - CR 103.5 — opening-hand actions resolve in turn order BEFORE the
///     first turn begins. The hand-snapshot is taken AFTER mulligan
///     resolution (matches the OpeningHandCheckEvent doc).
///   - CR 603.7 — delayed triggered abilities exist outside the printed-
///     ability zone restriction and auto-unregister after firing.
///   - CR 701.21 — exile is a one-way zone change, not a destroy.
///
/// ## Wiring
/// One instance attached at game-start by <see cref="GameDriver"/>,
/// alongside <see cref="OpeningHandLeylineAlternativeCost"/>. The
/// constructor takes the trigger manager so the scheduled first-upkeep
/// ability participates in the normal TriggerManager evaluation flow.
/// </summary>
public sealed class OpeningHandRevealLook4Trigger
{
    /// <summary>Marker keyword that flags a card as carrying the
    /// "reveal-from-opening-hand → schedule first-upkeep look-4-keep-1-on-top-
    /// exile-rest" rider. Factories ship this via
    /// <c>card.AddAbility(new KeywordAbility(RevealKeyword, card, owner))</c>.
    /// Today's only carrier is Devourer of Destiny; the surface is shared so
    /// future analogues (e.g. Conduit of Worlds, Chancellors with similar
    /// reveal shapes) drop in by adding the marker alone.</summary>
    public const string RevealKeyword = "OpeningHandRevealLook4";

    /// <summary>How many cards the scheduled trigger looks at — the
    /// printed N from "look at the top four cards of your library".</summary>
    public const int LookAtCount = 4;

    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _agents;
    private readonly TriggerManager? _triggers;

    public OpeningHandRevealLook4Trigger(
        IReadOnlyDictionary<Player, IPlayerAgent> agents,
        TriggerManager? triggers)
    {
        _agents = agents ?? throw new ArgumentNullException(nameof(agents));
        _triggers = triggers;
    }

    /// <summary>Wire this subscriber to the given event bus. Returns the
    /// async handler delegate so callers can unsubscribe if desired (tests
    /// typically let it ride to scope end).</summary>
    public Func<OpeningHandCheckEvent, Task> Attach(IEventBus bus)
    {
        ArgumentNullException.ThrowIfNull(bus);
        Func<OpeningHandCheckEvent, Task> handler = HandleAsync;
        bus.Subscribe(handler);
        return handler;
    }

    /// <summary>Handle an opening-hand check — drives the per-card reveal
    /// prompt loop and registers a delayed first-upkeep trigger per yes.
    /// Exposed for direct test invocation without constructing a full
    /// event bus.</summary>
    public async Task HandleAsync(OpeningHandCheckEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        if (!_agents.TryGetValue(evt.Player, out var agent) || agent == null)
        {
            // No agent bound — silently skip (defensive against partially-
            // wired test harnesses; GameDriver guarantees an agent per
            // player in real games).
            return;
        }

        // Snapshot tagged candidates up front in hand order. CR 103.5 —
        // "any player who wants to take such actions does so" — each
        // tagged card in the opening hand gets its own prompt.
        var candidates = new List<ICard>();
        foreach (var card in evt.OpeningHand)
        {
            if (HasRevealMarker(card)) candidates.Add(card);
        }

        foreach (var card in candidates)
        {
            // Defensive: skip if the card somehow left the hand mid-loop.
            if (card.Zone != ZoneType.Hand) continue;

            var answer = await agent.ChooseYesNoAsync(
                $"Reveal {card.Name} from your opening hand?",
                BotIntent.CardAdvantage);

            if (!answer) continue;

            ScheduleFirstUpkeepLook4(evt.Player, card);
        }
    }

    /// <summary>Register a one-shot delayed triggered ability that fires
    /// on the FIRST <see cref="StepStartedEvent"/>(Upkeep, revealer) — at
    /// which point it peeks the top <see cref="LookAtCount"/> cards of
    /// the revealer's library, prompts which (if any) to keep on top, and
    /// exiles the rest. CR 603.7d — delayed triggers auto-unregister
    /// after firing (handled inside <see cref="TriggerManager"/>).</summary>
    private void ScheduleFirstUpkeepLook4(Player revealer, ICard sourceCard)
    {
        // The closure captures the revealer + source card by reference so
        // the trigger fires only on THIS player's first upkeep (CR 500.2 —
        // each player has their own beginning-of-upkeep step), referencing
        // the printed source for stack-object naming.
        DelayedTriggeredAbility? delayed = null;

        // CR 500 / Triggers.OnStepBegin — "at the beginning of your first
        // upkeep" filtered to the revealer's upkeep step.
        var condition = Triggers.OnStepBegin(revealer, PhaseStateType.Upkeep);

        var effect = new Effect(
            $"{sourceCard.Name}: look at top {LookAtCount}, may keep 1 on top, exile the rest",
            () =>
            {
                var peeked = revealer.Zones.Library.GetCards().Take(LookAtCount).ToList();
                if (peeked.Count == 0) return;

                // CR 701.19a-style "you may" — controller agent picks one
                // to keep on top, or null = exile all four. ChooseLibraryPickAsync
                // is the canonical "pick zero-or-one from a candidate set
                // and can decline" surface (mirrors Path to Exile's tutor
                // rider). Use the revealer's registered agent.
                var pickAgent = _agents.TryGetValue(revealer, out var a) ? a : null;
                ICard? keep = pickAgent != null
                    ? pickAgent.ChooseLibraryPickAsync(
                            ctx: null, peeked, "card to keep on top of your library")
                        .GetAwaiter().GetResult()
                    : null;

                // Remove all peeked cards from the library first.
                foreach (var c in peeked) revealer.Zones.Library.RemoveCard(c);

                // Exile every peeked card that wasn't picked to keep on
                // top. CR 701.21 — exile is a zone change (Library → Exile).
                foreach (var c in peeked)
                {
                    if (ReferenceEquals(c, keep)) continue;
                    revealer.Zones.Exile.AddCard(c);
                    c.SetZone(ZoneType.Exile);
                }

                // Put the kept card back on top (index 0 = top per the
                // ScryAction convention used throughout the engine).
                if (keep != null)
                {
                    revealer.Zones.Library.InsertCardAt(0, keep);
                }
            });

        delayed = new DelayedTriggeredAbility(
            source: sourceCard,
            controller: revealer,
            condition: condition,
            effects: new IEffect[] { effect });

        _triggers?.RegisterDelayed(delayed);
    }

    private static bool HasRevealMarker(ICard card)
    {
        foreach (var ability in card.Abilities)
        {
            if (ability is KeywordAbility kw &&
                string.Equals(kw.Keyword, RevealKeyword, StringComparison.Ordinal))
            {
                return true;
            }
        }
        return false;
    }
}
