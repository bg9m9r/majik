using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.Game;

/// <summary>
/// CR 103.6 / CR 603.7 — Opening-hand reveal that schedules a
/// "at the beginning of your first main phase, add {mana}" trigger.
/// Currently scoped to the Chancellor-of-the-Tangle-style reveal clause:
///
///   "You may reveal this card from your opening hand. If you do, at the
///    beginning of your first main phase of the game, add {G}."
///
/// Modelled as a sibling of <see cref="OpeningHandRevealLook4Trigger"/>:
///   - Cards opt in by tagging themselves with
///     <see cref="KeywordAbility"/>(<see cref="RevealKeyword"/>) and
///     supplying the mana to add via
///     <see cref="ManaAmountByCard"/>. The keyword stores the mana-string
///     payload in <see cref="KeywordAbility.Parameter"/> so the subscriber
///     can be general without per-card subclasses.
///   - Subscriber listens for <see cref="OpeningHandCheckEvent"/>; for each
///     tagged card in the player's hand it asks
///     <see cref="IPlayerAgent.ChooseYesNoAsync"/> whether to reveal.
///   - On a yes-answer a <see cref="DelayedTriggeredAbility"/> is registered
///     with the supplied <see cref="TriggerManager"/>. Its condition is a
///     <see cref="StepStartedEvent"/> filter that fires the first time
///     <see cref="PhaseStateType.PreCombatMain"/> begins on the revealer's
///     turn — then auto-unregisters via TriggerManager's delayed-ability
///     sweep (Rule 603.7d).
///
/// ## CR citations
///   - CR 103.6 — "Some cards allow a player to take an action with them
///     from their opening hand" — opening-hand reveal is a pre-game action.
///   - CR 603.7 — delayed triggered abilities auto-unregister after firing.
///   - CR 106 / CR 605 — adding mana during the precombat main phase is a
///     special action; the mana is added directly to the player's pool
///     (simulated as an instantaneous pool-add since there is no priority
///     contention at trigger resolution).
///
/// ## Wiring
/// One instance attached at game-start by <see cref="GameDriver"/>,
/// alongside <see cref="OpeningHandRevealLook4Trigger"/>. The constructor
/// takes the trigger manager so the scheduled first-main-phase ability
/// participates in the normal TriggerManager evaluation flow.
/// </summary>
public sealed class OpeningHandRevealAddManaTrigger
{
    /// <summary>Marker keyword prefix that flags a card as carrying the
    /// "reveal-from-opening-hand → schedule first-precombat-main add-mana"
    /// rider. The mana payload is encoded directly in the keyword string as
    /// <c>"OpeningHandRevealAddMana:{G}"</c>. Factories ship this via:
    /// <c>card.AddAbility(new KeywordAbility($"{RevealKeywordPrefix}{{G}}", card, owner))</c>.
    ///
    /// Using a prefixed keyword (rather than <see cref="KeywordAbility.Arg"/>
    /// which is <c>int?</c> only) lets each carrier specify its own mana colour
    /// without subclassing. Today's only carrier is Chancellor of the Tangle
    /// ({G}); future Chancellors or analogues drop in by changing the suffix.
    /// </summary>
    public const string RevealKeywordPrefix = "OpeningHandRevealAddMana:";

    private readonly IReadOnlyDictionary<Player, IPlayerAgent> _agents;
    private readonly TriggerManager? _triggers;

    public OpeningHandRevealAddManaTrigger(
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
    /// prompt loop and registers a delayed first-main-phase trigger per yes.
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

        // Snapshot tagged candidates up front in hand order. CR 103.6 —
        // opening-hand reveals are optional ("you may"); each tagged card
        // gets its own prompt.
        var candidates = new List<(ICard Card, string ManaPayload)>();
        foreach (var card in evt.OpeningHand)
        {
            if (TryGetRevealPayload(card, out var manaPayload))
            {
                candidates.Add((card, manaPayload!));
            }
        }

        foreach (var (card, manaPayload) in candidates)
        {
            // Defensive: skip if the card somehow left the hand mid-loop.
            if (card.Zone != ZoneType.Hand) continue;

            var answer = await agent.ChooseYesNoAsync(
                $"Reveal {card.Name} from your opening hand?",
                BotIntent.Ramp);

            if (!answer) continue;

            ScheduleFirstPreCombatMainAddMana(evt.Player, card, manaPayload);
        }
    }

    /// <summary>Register a one-shot delayed triggered ability that fires
    /// on the FIRST <see cref="StepStartedEvent"/>(PreCombatMain, revealer).
    /// At that point it calls <see cref="Player.AddManaToPool"/> with the
    /// configured mana amount (CR 605 — mana abilities add mana to the pool).
    /// CR 603.7d — delayed triggers auto-unregister after firing (handled
    /// inside <see cref="TriggerManager"/>).</summary>
    private void ScheduleFirstPreCombatMainAddMana(
        Player revealer, ICard sourceCard, string manaPayload)
    {
        // The closure captures revealer + sourceCard; fires only on THIS
        // player's first PreCombatMain (CR 500.2 — each player has their
        // own beginning-of-precombat-main phase).
        DelayedTriggeredAbility? delayed = null;

        // "at the beginning of your first main phase" — PreCombatMain step
        // filtered to the revealer's own turn (same shape as Devourer's
        // first-upkeep trigger but targeting PreCombatMain instead).
        var condition = Triggers.OnStepBegin(revealer, PhaseStateType.PreCombatMain);

        var effect = new Effect(
            $"{sourceCard.Name}: add {manaPayload} (opening-hand reveal rider, CR 103.6)",
            () =>
            {
                // CR 605.1a — mana abilities resolve; the controller
                // receives the mana immediately. Parse once inside the
                // closure rather than at schedule time so the cost object
                // is never stale even if future code adjusts the mana
                // payload (currently it's always a constant string).
                revealer.AddManaToPool(ManaCost.Parse(manaPayload));
            });

        delayed = new DelayedTriggeredAbility(
            source: sourceCard,
            controller: revealer,
            condition: condition,
            effects: new IEffect[] { effect });

        _triggers?.RegisterDelayed(delayed);
    }

    private static bool TryGetRevealPayload(ICard card, out string? manaPayload)
    {
        foreach (var ability in card.Abilities)
        {
            if (ability is KeywordAbility kw &&
                kw.Keyword.StartsWith(RevealKeywordPrefix, StringComparison.Ordinal))
            {
                manaPayload = kw.Keyword[RevealKeywordPrefix.Length..];
                return true;
            }
        }
        manaPayload = null;
        return false;
    }
}
