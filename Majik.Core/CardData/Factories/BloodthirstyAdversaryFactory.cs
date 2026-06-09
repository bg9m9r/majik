using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Primitives;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Bloodthirsty Adversary (Innistrad: Midnight Hunt,
/// {1}{R}). Creature — Vampire 2/2. Oracle text (verified against the embedded
/// seed + Scryfall):
///   "Haste
///    When this creature enters, you may pay {2}{R} any number of times. When
///    you pay this cost one or more times, put that many +1/+1 counters on this
///    creature, then exile up to that many target instant and/or sorcery cards
///    with mana value 3 or less from your graveyard and copy them. You may cast
///    any number of the copies without paying their mana costs."
///
/// The "Adversary" cycle's signature shape: an ETB "you may pay {cost} any
/// number of times" scaling payoff (CR 603.2 reflexive trigger). The repeatable
/// {2}{R} payment count N drives the payoffs: N +1/+1 counters and up-to-N
/// instant/sorcery cards (mv ≤ 3) recurred from your graveyard for free.
///
/// ## How the two binder-chain primitives are realised
///
/// <b>Primitive 1 — resolution-time repeatable optional mana payment.</b>
/// "You may pay {2}{R} any number of times" is paid when the ETB trigger
/// RESOLVES, not as a cast-time additional cost — so the count is decided live
/// by the controller's agent during resolution, and there is no cast-time tally
/// to read. (This is distinct from <see cref="Majik.Core.Costs.MultikickerAdditionalCost"/>,
/// whose count locks at announcement and is cleared at battlefield entry before
/// a creature's ETB trigger resolves.) The resolution-time loop is
/// <see cref="RepeatableManaPayment.PromptAsync"/>: prompt yes/no + drain
/// {2}{R} until the agent declines or can't pay; the count N it returns scales
/// the payoff.
///
/// <b>Primitive 2 — cast an instant/sorcery from your graveyard for free.</b>
/// Reuses the runtime-flashback grant that Snapcaster Mage already wires into
/// production (<see cref="Card.GrantRuntimeFlashback"/> +
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost"/>), but at a {0} cost
/// — "without paying their mana costs". For each chosen graveyard target the
/// ETB stamps a free flashback grant; the priority loop then surfaces the cast
/// (via <see cref="Majik.Core.Players.Agents.RuntimeFlashbackAltCostProbe"/>),
/// the spell goes on the stack and resolves through the REAL
/// <see cref="Majik.Core.Game.SpellCastFlow"/> with real targeting, and
/// <see cref="Majik.Core.Costs.FlashbackAlternativeCost.OnResolved"/> exiles the
/// card afterward (CR 702.34b) — matching the printed "exile … and cast"
/// outcome. The grant expires at the next Cleanup step (CR 514.2), exactly as
/// Snapcaster's does, so the free cast is bounded to this turn.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — canonical build. Attaches Haste + the ETB
///   <see cref="TriggeredAbility"/> (a real ability in <c>card.Abilities</c> so
///   the pool-wide audit no longer flags MissingTrigger). The ETB reads the
///   pay-count + targets from the live <see cref="ResolutionContext"/> at
///   resolution, so it works on the shape build too (no game context ⇒ N == 0
///   ⇒ clean no-op).
/// - <see cref="Create(Player, ContinuousEffectsService?)"/> — the effects-aware
///   overload the source generator recognises and the production
///   <c>GameFacade</c> routed build dispatches to. Forwards to the canonical
///   overload but threads <c>effects.EventBus</c> so the flashback grants get
///   their CR 514.2 end-of-turn cleanup hook — the Festival Crasher /
///   Stormbreath Dragon prod-wiring pattern.
///
/// ## Deferred (v1 gaps)
/// - <b>"Copy them" vs cast-from-graveyard</b>: the printed card exiles the
///   originals and casts copies; this implementation casts the original from
///   the graveyard for free and exiles it on resolution. The net game outcome
///   (each chosen spell's effect happens once for free; the card ends in exile)
///   matches — the same accepted v1 simplification Snapcaster Mage makes (grant
///   flashback rather than re-instantiate a distinct copy stack object). A real
///   spell-copy stack object is the shared follow-up tracked on
///   <see cref="Majik.Core.Services.SpellCopier"/>.
/// </summary>
[CardName("Bloodthirsty Adversary")]
public static class BloodthirstyAdversaryFactory
{
    public const string CardName = "Bloodthirsty Adversary";
    public const string Slug = "bloodthirsty-adversary";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>The repeatable ETB payment — {2}{R} per payment (CR 603.2).</summary>
    public const string PayCostText = "{2}{R}";

    /// <summary>Max mana value of a graveyard instant/sorcery this can recur.</summary>
    public const int MaxTargetManaValue = 3;

    /// <summary>{2}{R} — the per-iteration ETB payment cost.</summary>
    public static ManaCost PayCost => ManaCost.Parse(PayCostText);

    /// <summary>{0} — the free flashback cost the recurred spell is granted
    /// ("without paying their mana costs", CR 601.3b).</summary>
    public static ManaCost FreeCost => ManaCost.Parse("{0}");

    private static readonly CardDefinition Definition =
        CardDefinitionLoader.FromEmbeddedResource(Slug);

    /// <summary>
    /// Canonical build: Creature — Vampire, {1}{R}, 2/2, Haste, plus the ETB
    /// reflexive trigger. This is the overload <see cref="NamedCardFactory"/>
    /// dispatches to. No event bus ⇒ the flashback grants the ETB stamps have
    /// no auto-cleanup hook (callers manage end-of-turn manually); the count +
    /// counters + grant still apply.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, eventBus: null);

    /// <summary>
    /// Effects-aware overload the source generator recognises and the
    /// <b>production</b> <c>GameFacade</c> routed build dispatches to (via
    /// <see cref="NamedCardFactory.Create(string, Player, ContinuousEffectsService?)"/>).
    /// Bloodthirsty registers no continuous effect, but the ETB's flashback
    /// grants need an <see cref="IEventBus"/> for their CR 514.2 end-of-turn
    /// cleanup, so this forwards the bus from <c>effects.EventBus</c>. Without
    /// this overload the routed build would fall through to single-arg dispatch
    /// and the ETB trigger would be absent in live play (the bug the pool-wide
    /// audit flags as MissingTrigger) — same fix as Festival Crasher /
    /// Stormbreath Dragon.
    /// </summary>
    public static Creature Create(Player owner, ContinuousEffectsService? effects) =>
        Create(owner, effects?.EventBus);

    /// <summary>
    /// Construct Bloodthirsty Adversary, attaching the ETB reflexive trigger.
    /// When <paramref name="eventBus"/> is supplied the flashback grants the ETB
    /// stamps are cleared on the next Cleanup step (CR 514.2); when null the
    /// grants persist (shape / direct-call path).
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = (Creature)CardDefinitionFactory.Build(Definition, owner);

        // CR 702.10 — Haste. KeywordAbility marker, same shape as every other
        // JSON-backed keyworded creature.
        card.AddAbility(new KeywordAbility("Haste", card, owner));

        // ----------------------------------------------------------------
        // CR 603.2 — the ETB reflexive trigger.
        //   "When this creature enters, you may pay {2}{R} any number of
        //    times. When you pay this cost one or more times, put that many
        //    +1/+1 counters on this creature, then exile up to that many
        //    target instant and/or sorcery cards with mana value 3 or less
        //    from your graveyard and copy them. You may cast any number of
        //    the copies without paying their mana costs."
        //
        // The repeatable payment + the up-to-N target choice are decided at
        // RESOLUTION off the live ResolutionContext (agent + game), so the
        // single TriggeredAbility works on the shape build AND the prod routed
        // build with no captured services.
        // ----------------------------------------------------------------
        TriggeredAbility? etb = null;
        var condition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, card) && e.ToZone == ZoneType.Battlefield);

        var etbEffect = new Effect(
            $"{CardName}: pay {{2}}{{R}} ×N ⇒ N +1/+1 counters + recur up to N instant/sorcery (mv≤{MaxTargetManaValue}) from your graveyard for free",
            async ctx =>
            {
                var controller = card.Controller ?? owner;

                // Primitive 1 — resolution-time repeatable optional payment.
                // No live decision surface (shape-only resolve) ⇒ N == 0.
                var n = await RepeatableManaPayment
                    .PromptAsync(
                        controller, ctx.Agent, ctx.Game, PayCost,
                        promptText: $"{CardName}: pay {PayCostText} again?",
                        ct: ctx.Ct)
                    .ConfigureAwait(false);

                // CR 603.2 — the reflexive "when you pay this cost one or more
                // times" only fires when N ≥ 1. N == 0 ⇒ clean no-op.
                if (n == 0) return;

                // "put that many +1/+1 counters on this creature" (CR 122).
                if (card.Zone == ZoneType.Battlefield)
                {
                    card.Counters.Add(CounterType.PlusOnePlusOne, n);
                }

                // "exile up to that many target instant and/or sorcery cards
                // with mana value 3 or less from your graveyard" — up-to-N
                // target choice resolved through the agent at resolution
                // (CR 601.2c / 603.3d). No agent ⇒ no recursion (counters
                // already applied above).
                if (ctx.Agent == null || ctx.Game == null) return;

                var legal = LegalTargets(controller);
                if (legal.Count == 0) return;

                var request = new TargetRequest(
                    Description: $"up to {n} target instant and/or sorcery card(s) with mana value {MaxTargetManaValue} or less from your graveyard",
                    MinTargets: 0,
                    MaxTargets: n,
                    LegalCandidates: legal.Cast<object>().ToList(),
                    Intent: BotIntent.Reanimate);

                var chosen = await ctx.Agent
                    .ChooseTargetsAsync(ctx.Game, request, ctx.Ct)
                    .ConfigureAwait(false);

                var taken = 0;
                foreach (var raw in chosen)
                {
                    if (taken >= n) break; // "up to that many"
                    if (raw is not Card target) continue;

                    // CR 608.2b — illegal-on-resolution recheck.
                    if (!IsLegalGraveyardTarget(target, controller)) continue;

                    // Primitive 2 — grant a FREE flashback ({0}) so the
                    // controller casts the spell from the graveyard without
                    // paying its mana cost (CR 601.3b); FlashbackAlternativeCost
                    // exiles it on resolution (CR 702.34b). The priority loop
                    // surfaces the cast (RuntimeFlashbackAltCostProbe).
                    target.GrantRuntimeFlashback(FreeCost);
                    taken++;

                    // CR 514.2 — clear the grant at the next Cleanup step so
                    // the free cast is bounded to this turn (matches Snapcaster).
                    ScheduleEndOfTurnClear(eventBus, target);
                }
            });

        etb = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etb);

        return card;
    }

    /// <summary>
    /// CR 514.2 — subscribe a one-shot Cleanup-step handler that clears the
    /// runtime flashback grant on <paramref name="target"/> and unsubscribes
    /// itself. No bus (shape / direct-call path) ⇒ the grant persists until the
    /// caller clears it.
    /// </summary>
    private static void ScheduleEndOfTurnClear(IEventBus? eventBus, Card target)
    {
        if (eventBus == null) return;

        Action<StepStartedEvent>? handler = null;
        handler = e =>
        {
            if (e.StepType != StepStateType.Cleanup) return;
            target.ClearRuntimeFlashback();
            if (handler != null) eventBus.Unsubscribe(handler);
        };
        eventBus.Subscribe(handler);
    }

    /// <summary>
    /// CR 608.2b illegal-target recheck for a chosen graveyard card: still in
    /// <paramref name="controller"/>'s graveyard, owned by the controller, an
    /// instant or sorcery, and mana value ≤ 3.
    /// </summary>
    private static bool IsLegalGraveyardTarget(Card card, Player controller)
    {
        if (card.Zone != ZoneType.Graveyard) return false;
        if (!ReferenceEquals(card.Owner, controller)) return false;
        if (!card.HasType(CardType.Instant) && !card.HasType(CardType.Sorcery)) return false;
        return card.ManaCostValue.TotalValue <= MaxTargetManaValue;
    }

    /// <summary>
    /// The candidate pool for the "up to N target instant/sorcery cards with
    /// mana value 3 or less from your graveyard" request.
    /// </summary>
    public static IReadOnlyList<Card> LegalTargets(Player controller)
    {
        ArgumentNullException.ThrowIfNull(controller);
        return controller.Zones.Graveyard.GetCards()
            .OfType<Card>()
            .Where(c => IsLegalGraveyardTarget(c, controller))
            .ToList();
    }
}
