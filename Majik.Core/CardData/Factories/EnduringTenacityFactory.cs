using Majik.Core.Abilities;
using Majik.Core.CardData.Definitions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Enduring Tenacity (Duskmourn: House of Horror,
/// {2}{B}{B}). Enchantment Creature — Snake Glimmer 4/3. Oracle text
/// (verified against Scryfall):
///   "Whenever you gain life, target opponent loses that much life.
///    When Enduring Tenacity dies, if it was a creature, return it to the
///    battlefield under its owner's control. It's an enchantment. (It's not a
///    creature.)"
///
/// The base shape (name, Creature + Enchantment types, Snake + Glimmer
/// subtypes, {2}{B}{B}, 4/3) is materialised from the embedded JSON definition
/// (<c>enduring-tenacity.json</c>) via
/// <see cref="CardDefinitionLoader.FromEmbeddedResource"/> +
/// <see cref="CardDefinitionFactory.Build"/>. The JSON declares no abilities —
/// the lifegain-drain trigger and the dies → return-as-enchantment trigger are
/// layered on here (same JSON-backed-identity + code-attached-behaviour posture
/// as <see cref="EnduringCuriosityFactory"/>, its sibling in the "Enduring"
/// cycle).
///
/// ## Implemented (v1)
///
/// - <b>"Whenever you gain life, target opponent loses that much life."
///   (CR 119.3 / CR 603.6a / CR 603.7)</b>: a <see cref="TriggeredAbility"/>
///   over <see cref="LifeChangedEvent"/> filtered (via
///   <see cref="Triggers.OnLifeGainedByPlayer"/>) to the controller AND
///   strictly-positive deltas (NewLife &gt; PreviousLife — life *gain*, not
///   life loss). The "that much" amount (CR 603.7 — snapshotted when the
///   trigger queues) is captured by an <see cref="IEventBus"/> subscription
///   that records the most recent <c>NewLife - PreviousLife</c> delta into a
///   closure-shared mutable slot; the trigger Effect reads + clears the slot
///   on resolution. The "target opponent" clause drains that amount from the
///   live resolution context's opponents (<see cref="ContextOpponents"/>) —
///   the same posture as <see cref="CliffhavenVampireFactory"/> /
///   <see cref="VitoThornOfTheDuskRoseFactory"/> (in a two-player game the
///   chosen target is the sole opponent; multiplayer target-choice is a v1 gap
///   — see below).
///
/// - <b>Dies → return as an enchantment (CR 603.6c / CR 701.20 / CR 205.2 /
///   613.1d)</b>: a <see cref="TriggeredAbility"/> over
///   <see cref="Triggers.OnDies"/> with <c>activeZones = {Battlefield,
///   Graveyard}</c> so the trigger survives the death zone-move. The printed
///   "if it was a creature" intervening-if is satisfied because the card is
///   still a creature when it dies (the type-strip only applies AFTER the
///   return). On resolution the card is returned from the graveyard to the
///   battlefield under its owner's control
///   (<see cref="Fx.ReturnFromGraveyardToBattlefield"/>, ZoneService-routed
///   when supplied so ETB triggers fire per CR 603.6a) and a captured
///   <c>hasReturned</c> flag flips true, which gates a
///   <see cref="Layer4TypeStripEffect"/> registered at construction. From that
///   point the Layer-4 effect strips <see cref="CardType.Creature"/> from the
///   card's layered characteristics — "It's an enchantment. (It's not a
///   creature.)" — exactly the machinery <see cref="EnduringCuriosityFactory"/>
///   uses. Once returned, a subsequent death finds the intervening-if false
///   (it's no longer a creature) so it stays in the graveyard.
///
/// ## Deferred (v1 gaps)
///
/// - <b>Multiplayer "target opponent" choice</b>: the engine models the drain
///   as the live opponent set (correct + unambiguous in two-player). Explicit
///   single-target selection in 3+-player games is the shared
///   target-opponent-loses-life gap (Cliffhaven Vampire / Vito).
/// - <b>Shape-only path</b>: without an <see cref="IEventBus"/> wiring the
///   amount slot is never stamped, so the drain clause no-ops on hand-executed
///   Effects. Tests assert the drain shape either by wiring a bus or via the
///   <see cref="SetPendingGainAmount(Creature, int)"/> test hook (the live
///   engine wire-up site always passes a bus).
/// </summary>
[CardName("Enduring Tenacity")]
public static class EnduringTenacityFactory
{
    public const string CardName = "Enduring Tenacity";
    public const string Slug = "enduring-tenacity";

    // Identity-keyed slot for the "that much" amount snapshot. Stamped by the
    // event-bus subscription (or via the test hook) and consumed + cleared by
    // the trigger Effect at resolution. Mirrors VitoThornOfTheDuskRoseFactory.
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Creature, AmountSlot>
        _pendingAmounts = new();

    private sealed class AmountSlot
    {
        public int Amount;
    }

    /// <summary>
    /// Construct Enduring Tenacity with no live runtime services. Both triggers
    /// are attached for shape inspection. This is the overload
    /// <see cref="NamedCardFactory"/> dispatches to.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null, continuousEffects: null, zoneService: null);

    /// <summary>
    /// Construct Enduring Tenacity with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">When supplied, the card subscribes to
    /// <see cref="LifeChangedEvent"/> so the "that much" amount slot is stamped
    /// before the lifegain trigger resolves.</param>
    /// <param name="triggers">When supplied, both triggered abilities are
    /// registered so the matching events land them on the stack
    /// automatically.</param>
    /// <param name="continuousEffects">When supplied, the
    /// <see cref="Layer4TypeStripEffect"/> backing "It's an enchantment. (It's
    /// not a creature.)" is registered on this service (gated OFF until the card
    /// has returned via the dies trigger).</param>
    /// <param name="zoneService">When supplied, the dies trigger's graveyard →
    /// battlefield return routes through <see cref="ZoneService.MoveCard"/> so
    /// ETB triggers fire (CR 603.6a); raw-zone fallback otherwise.</param>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers,
        ContinuousEffectsService? continuousEffects,
        ZoneService? zoneService)
    {
        ArgumentNullException.ThrowIfNull(owner);

        // Base shape from the embedded JSON definition (name, Creature +
        // Enchantment types, Snake + Glimmer subtypes, {2}{B}{B}, 4/3). The JSON
        // carries no abilities — both triggers are layered on below.
        var definition = CardDefinitionLoader.FromEmbeddedResource(Slug);
        var card = (Creature)CardDefinitionFactory.Build(definition, owner);

        // Pre-allocate the amount slot so SetPendingGainAmount + the event-bus
        // subscription share one identity-keyed cell.
        var slot = new AmountSlot { Amount = 0 };
        _pendingAmounts.AddOrUpdate(card, slot);

        // ----------------------------------------------------------------
        // Event-bus subscription — stamp the "that much" amount BEFORE the
        // trigger queues / resolves. Filtered to controller-scoped gains
        // (CR 603.7 — the value is snapshotted when the trigger fires).
        // ----------------------------------------------------------------
        if (eventBus != null)
        {
            eventBus.Subscribe<LifeChangedEvent>(e =>
            {
                if (!ReferenceEquals(e.Player, card.Controller ?? owner)) return;
                var delta = e.NewLife - e.PreviousLife;
                if (delta <= 0) return;
                slot.Amount = delta;
            });
        }

        // ----------------------------------------------------------------
        // "Whenever you gain life, target opponent loses that much life."
        //   CR 119.3 / 603.6a / 603.7.
        // Triggers.OnLifeGainedByPlayer filters LifeChangedEvent to the
        // controller AND NewLife > PreviousLife (strictly-positive delta).
        // Resolution: drain slot.Amount from the live resolution context's
        // opponents (ContextOpponents) — read LIVE, not via a captured resolver
        // (resolver-null bug class; mirrors Vito #2543 / Cliffhaven). The slot
        // is reset to 0 after the drain so a stale value can't replay.
        // ----------------------------------------------------------------
        var drainEffect = new Effect(
            $"{CardName}: target opponent loses that much life",
            ctx =>
            {
                var amount = slot.Amount;
                slot.Amount = 0;
                if (amount <= 0) return ValueTask.CompletedTask;

                var controller = card.Controller ?? owner;
                foreach (var opp in ContextOpponents.Of(ctx, controller))
                {
                    opp.LoseLife(amount);
                }
                return ValueTask.CompletedTask;
            });

        var lifegainTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLifeGainedByPlayer(owner),
            effects: new IEffect[] { drainEffect },
            activeZones: new[] { ZoneType.Battlefield });
        card.AddAbility(lifegainTrigger);
        triggers?.RegisterTriggeredAbility(lifegainTrigger);

        // Captured "the card has returned and is now a non-creature
        // enchantment" flag. Flipped true by the dies trigger after the return;
        // read by both the Layer-4 type-strip predicate and the dies trigger's
        // intervening-if re-check.
        var hasReturned = false;

        // ----------------------------------------------------------------
        // "When Enduring Tenacity dies, if it was a creature, return it to the
        //  battlefield under its owner's control. It's an enchantment. (It's
        //  not a creature.)" (CR 603.6c / CR 701.20 / CR 205.2 / 613.1d).
        // ----------------------------------------------------------------
        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[]
            {
                Fx.Inline(
                    $"{CardName}: dies — if it was a creature, return it as a (non-creature) enchantment",
                    () => ReturnAsEnchantment(card, zoneService, ref hasReturned)),
            },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });
        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        // ----------------------------------------------------------------
        // Layer 4 type-strip backing "It's an enchantment. (It's not a
        // creature.)" — CR 205.2 / 613.1d. Registered up-front but gated OFF by
        // the captured hasReturned flag, so the card is a normal creature until
        // the dies trigger returns it. Source-anchored: inert once the card
        // LTBs. Same machinery as EnduringCuriosityFactory.
        // ----------------------------------------------------------------
        if (continuousEffects != null)
        {
            card.ActiveEffects = continuousEffects;
            continuousEffects.Register(new Layer4TypeStripEffect(
                source: card,
                predicate: () => hasReturned));
        }

        return card;
    }

    /// <summary>
    /// Resolve the dies trigger: if the card was a creature when it died, return
    /// it from the graveyard to the battlefield under its owner's control and
    /// flip <paramref name="hasReturned"/> so the Layer-4 type-strip engages.
    /// Exposed for direct invocation by tests.
    /// </summary>
    public static void ReturnAsEnchantment(
        Creature card,
        ZoneService? zoneService,
        ref bool hasReturned)
    {
        ArgumentNullException.ThrowIfNull(card);

        // CR 603.6c — intervening "if": only return if it was still a creature
        // when it died. Once it has already returned as a (non-creature)
        // enchantment, a subsequent death fails this check, so it stays put.
        if (hasReturned) return;

        // CR 608.2 — the card must still be in the graveyard at resolution.
        if (card.Zone != ZoneType.Graveyard) return;

        var owner = card.Owner;
        if (owner == null) return;

        // CR 701.20 — graveyard → battlefield under its owner's control.
        Fx.ReturnFromGraveyardToBattlefield(card, owner, zoneService);
        if (card.Zone != ZoneType.Battlefield) return;

        // CR 205.2 / 613.1d — from now on "It's an enchantment. (It's not a
        // creature.)" The Layer4TypeStripEffect registered at construction reads
        // this flag and strips the Creature type on every Compute pass.
        hasReturned = true;
    }

    /// <summary>
    /// Test hook — stamp the pending "that much" amount on
    /// <paramref name="tenacity"/> directly. Shape-only tests use this to assert
    /// the drain body without wiring an <see cref="IEventBus"/>.
    /// </summary>
    public static void SetPendingGainAmount(Creature tenacity, int amount)
    {
        ArgumentNullException.ThrowIfNull(tenacity);
        if (_pendingAmounts.TryGetValue(tenacity, out var slot))
        {
            slot.Amount = amount;
        }
    }
}
