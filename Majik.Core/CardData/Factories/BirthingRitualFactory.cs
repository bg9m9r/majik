using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Random;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Birthing Ritual (Foundations, {1}{G}).
///
/// Enchantment. Oracle text:
///   "At the beginning of your end step, if you control a creature, look
///    at the top seven cards of your library. Then you may sacrifice a
///    creature. If you do, you may put a creature card with mana value
///    X or less from among those cards onto the battlefield, where X is
///    1 plus the sacrificed creature's mana value. Put the rest on the
///    bottom of your library in a random order."
///
/// ## Implemented (v1)
///
/// - Enchantment shape, mana cost <c>{1}{G}</c>.
/// - End-step trigger gated to <b>controller's</b> end step via
///   <see cref="Triggers.OnStepBegin"/> with the controller filter
///   (CR 500.7 — "your end step"; matches Soulherder / Ajani Nacatl
///   Pariah front-face shape).
/// - <b>Intervening-if</b> (CR 603.4) — "if you control a creature." The
///   <see cref="TriggeredAbility.InterveningIf"/> predicate counts
///   <see cref="Creature"/>s the live controller controls. Stops the
///   trigger from entering the stack when the count is zero; re-checked
///   defensively at resolve time per CR 603.4's second-pass requirement
///   (mirrors <see cref="FieldOfTheDeadFactory"/>).
/// - <b>Resolution</b> body:
///   1. Peek the top seven of the controller's library — short library
///      is fine (CR 701.21 "top N" never throws; matches
///      <see cref="CollectedCompanyFactory"/>'s peek shape).
///   2. Prompt the controller's agent (via
///      <see cref="IPlayerAgent.ChooseFromBattlefieldAsync"/> with
///      <see cref="BotIntent.CheatIntoPlay"/>) to optionally pick one of
///      their creatures to sacrifice. Null = decline ("then you MAY
///      sacrifice"); no eligible creature = same path.
///   3. If a creature is sacrificed, route it through
///      <see cref="OracleSpellBinder.MoveToGraveyard"/> with
///      <see cref="ZoneMoveReason.Sacrifice"/> (bypasses indestructible
///      per CR 702.12b; same path Ajani Nacatl Pariah uses). Compute
///      <c>X = 1 + ManaCost.Parse(sac.ManaCost).TotalValue</c>
///      (CR 202.3 — mana value reads off the printed cost).
///   4. If a sacrifice happened, prompt
///      <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> to optionally
///      pick a <see cref="CardType.Creature"/> card with
///      <c>mv ≤ X</c> from the peeked set. The factory defensively
///      validates the pick is a member of the offered candidates so an
///      illegal pick (e.g. higher-MV) falls through to "decline" rather
///      than smuggling an over-cost creature onto the battlefield.
///   5. Move the picked creature Library → Battlefield via
///      <see cref="ZoneService.MoveCard"/> when a service is registered
///      (so ETB triggers fire — CR 603.6a); raw zone fallback otherwise.
///      Same routing as <see cref="ChordOfCallingFactory"/> /
///      <see cref="CollectedCompanyFactory"/>.
///   6. Bottom the remaining peeked cards (everything not put onto the
///      battlefield) in random order. Random order sourced from
///      <see cref="GameRandomRegistry.Get"/> — deterministic when tests
///      seed it; same posture as
///      <see cref="CollectedCompanyFactory"/>.
///
/// ## Ordering of the two "may" clauses (CR 603.6)
/// The printed text is "Then you may sacrifice a creature. If you do,
/// you may put …" — the second "may" is gated on the sacrifice
/// happening. A "may sacrifice" decline therefore short-circuits the
/// put-onto-battlefield step entirely; in that case all seven peeked
/// cards are bottomed.
///
/// ## Sacrifice still happens even if the put-onto-battlefield is
/// declined
/// The second "may" is independent of the first. If the controller
/// sacrifices but the library pick is null (or no eligible candidate
/// exists), the sacrifice still resolves (CR 603 — costs that are part
/// of the resolution body don't roll back), and the seven peeked cards
/// are bottomed.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Bot-side library-pick quality</b>: the picked creature is the
///   agent's choice. Heuristic / EV-search agents score via
///   <see cref="IPlayerAgent.ChooseLibraryPickAsync"/> and the
///   <c>LibraryPickPolicy</c> registry — this factory delegates without
///   per-card tuning.
/// - <b>Reveal events</b>: the peek does not publish a per-card reveal
///   event. Same gap as the rest of the look-at-top-N family
///   (<see cref="CollectedCompanyFactory"/> / <see cref="AncientStirringsFactory"/>).
/// </summary>
[CardName("Birthing Ritual")]
public static class BirthingRitualFactory
{
    public const string CardName = "Birthing Ritual";
    public const string PrintedManaCost = "{1}{G}";
    public const int PeekCount = 7;

    /// <summary>
    /// Construct Birthing Ritual with no live trigger-manager wiring
    /// (shape / dispatcher path). The end-step trigger is attached to
    /// the card so shape / dispatcher tests see the ability, but it is
    /// not registered with any <see cref="TriggerManager"/>.
    /// </summary>
    public static Enchantment Create(Player owner)
        => Create(owner, triggers: null, zoneService: null);

    /// <summary>
    /// Construct Birthing Ritual with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the end-step trigger is
    /// registered so <see cref="Events.StepStartedEvent"/> automatically
    /// queues the ability (CR 603.2). When
    /// <paramref name="zoneService"/> is supplied, the resolve-time
    /// Library → Battlefield move publishes
    /// <see cref="Events.CardMovedEvent"/> so ETB triggers fire on the
    /// tutored creature (CR 603.6a).
    /// </summary>
    public static Enchantment Create(
        Player owner,
        TriggerManager? triggers,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Enchantment(CardName, PrintedManaCost);
        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // End-step trigger — CR 500.4 / CR 603.1 + CR 603.4
        //   "At the beginning of your end step, if you control a creature,
        //    look at the top seven cards of your library. Then you may
        //    sacrifice a creature. If you do, you may put a creature card
        //    with mana value X or less from among those cards onto the
        //    battlefield, where X is 1 plus the sacrificed creature's
        //    mana value. Put the rest on the bottom of your library in
        //    a random order."
        // ----------------------------------------------------------------
        var resolveEffect = new Effect(
            $"{CardName}: end-step look at top 7, may-sac → may-put creature card with mv ≤ X+1, bottom rest randomly",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;

                // CR 603.4 second-pass: re-check the intervening-if at
                // resolution. If the controller no longer controls a
                // creature, the ability does nothing (matches
                // FieldOfTheDeadFactory).
                var controller = card.Controller ?? owner;
                if (!ControllerHasCreature(controller)) return;

                Resolve(controller, zoneService);
            });

        var endStepTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnStepBegin(owner, Majik.Core.StateMachine.PhaseStateType.End),
            effects: new IEffect[] { resolveEffect },
            interveningIf: () => ControllerHasCreature(card.Controller ?? owner),
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(endStepTrigger);
        triggers?.RegisterTriggeredAbility(endStepTrigger);

        return card;
    }

    /// <summary>
    /// Execute Birthing Ritual's resolution body against
    /// <paramref name="controller"/>'s library + battlefield. Public so
    /// tests and bots can drive resolution without going through the
    /// trigger manager / stack.
    /// </summary>
    /// <param name="controller">Player whose library is peeked, whose
    /// creature is sacrificed (if accepted), and onto whose battlefield
    /// the picked creature lands.</param>
    /// <param name="zoneService">Optional zone service for routing the
    /// Library → Battlefield move so ETB triggers fire.</param>
    /// <param name="agent">Optional explicit agent that owns the
    /// "may sacrifice" + "may put" decisions. When null, falls back to
    /// <see cref="AgentRegistry.Get"/>; when no agent is registered
    /// either, declines both decisions (deterministic conservative
    /// fallback — same posture as <see cref="CollectedCompanyFactory"/>
    /// for the agentless test path).</param>
    public static void Resolve(
        Player controller,
        ZoneService? zoneService = null,
        IPlayerAgent? agent = null)
    {
        ArgumentNullException.ThrowIfNull(controller);

        var library = controller.Zones.Library;

        // CR 603.4 — body-level intervening-if defence (matches the
        // trigger-level predicate). With zero creatures the printed text
        // has no creature to sacrifice and no card-to-put dependency, so
        // we degenerate to a pure "look-at-top-7-and-bottom-random"
        // no-op. (Normally the intervening-if would have already gated
        // stack entry — this is the safety net for direct callers.)
        if (!ControllerHasCreature(controller))
        {
            // Conservative no-op: leave the library alone (matches
            // FieldOfTheDeadFactory's second-pass defence).
            return;
        }

        // 1. Peek up to PeekCount cards (CR 701.21 — short library is fine).
        var peeked = library.GetCards().Take(PeekCount).ToList();

        // 2. "Then you may sacrifice a creature." Prompt the controller's
        //    agent. Empty candidate list (no creatures) is a no-op for the
        //    rest of the body — we still bottom the peek randomly.
        agent ??= AgentRegistry.Get(controller);

        var sacCandidates = controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Cast<ICard>()
            .ToList();

        ICard? sacrificed = null;
        if (sacCandidates.Count > 0 && agent != null)
        {
            // BotIntent.CheatIntoPlay — the upside of the sac is getting
            // a (usually larger) creature onto the battlefield without
            // paying its mana cost. Heuristic / EV agents score the
            // tradeoff via the live agent.
            var pick = agent.ChooseFromBattlefieldAsync(
                chooser: controller,
                candidates: sacCandidates,
                intent: BotIntent.CheatIntoPlay)
                .GetAwaiter().GetResult();

            // CR 117 — "may" allows null. Defensive: only accept picks
            // from the offered candidates.
            if (pick != null && sacCandidates.Contains(pick))
            {
                sacrificed = pick;
            }
        }

        // 3. If a sacrifice happened, route it to the graveyard and
        //    compute X = 1 + sac.MV.
        int xCap = 0;
        if (sacrificed != null)
        {
            xCap = 1 + ManaCost.Parse(sacrificed.ManaCost ?? string.Empty).TotalValue;

            // CR 701.16 — sacrifice. Bypasses Indestructible per CR 702.12b.
            // Same routing as Ajani Nacatl Pariah / All Is Dust.
            OracleSpellBinder.MoveToGraveyard(sacrificed, ZoneMoveReason.Sacrifice);
        }

        // 4. If a sacrifice happened, prompt for the "may put a creature
        //    card with mv ≤ X from the peek onto the battlefield."
        ICard? putPick = null;
        if (sacrificed != null)
        {
            bool IsEligible(ICard c) =>
                c.HasType(CardType.Creature) &&
                ManaCost.Parse(c.ManaCost ?? string.Empty).TotalValue <= xCap;

            var libraryCandidates = peeked.Where(IsEligible).ToList();
            if (libraryCandidates.Count > 0 && agent != null)
            {
                var pick = agent.ChooseLibraryPickAsync(
                    ctx: null,
                    candidates: libraryCandidates,
                    kindLabel: $"creature card with mana value {xCap} or less")
                    .GetAwaiter().GetResult();

                // CR 117 — "may" allows null. Defensive: the pick must be
                // in the eligibility-filtered set so an over-cost choice
                // is rejected silently (matches CollectedCompanyFactory).
                if (pick != null && libraryCandidates.Contains(pick))
                {
                    putPick = pick;
                }
            }
        }

        // 5. Move the picked creature Library → Battlefield.
        if (putPick != null)
        {
            var effectiveZones = zoneService ?? ZoneServiceRegistry.Get(controller);
            if (effectiveZones != null)
            {
                effectiveZones.MoveCard(
                    putPick, ZoneType.Library, ZoneType.Battlefield, controller);
            }
            else
            {
                // Raw-zone fallback — same shape as
                // CollectedCompanyFactory's agentless path. ETB triggers
                // won't fire because no event publishes.
                library.RemoveCard(putPick);
                controller.Zones.Battlefield.AddCard(putPick);
                putPick.SetZone(ZoneType.Battlefield);
                putPick.SetController(controller);
            }
        }

        // 6. Bottom the remaining peeked cards (everything not put onto
        //    the battlefield) in random order. Per-game RNG; tests seed
        //    it if they need a deterministic order. Same posture as
        //    CollectedCompanyFactory's bottom-in-random step.
        var remainder = peeked.Where(c => !ReferenceEquals(c, putPick)).ToList();
        if (remainder.Count > 0)
        {
            var rng = GameRandomRegistry.Get(controller);
            rng.Shuffle(remainder);

            foreach (var c in remainder)
            {
                library.RemoveCard(c);
            }
            foreach (var c in remainder)
            {
                library.AddCard(c);
                c.SetZone(ZoneType.Library);
            }
        }
    }

    /// <summary>
    /// CR 603.4 predicate — does <paramref name="controller"/> control at
    /// least one Creature on the battlefield? Counts every creature
    /// (including the sacrifice "fodder" — Birthing Ritual itself is an
    /// Enchantment, never a creature).
    /// </summary>
    public static bool ControllerHasCreature(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Battlefield.GetCards().OfType<Creature>().Any();
    }
}
