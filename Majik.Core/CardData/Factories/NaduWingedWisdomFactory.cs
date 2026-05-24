using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Nadu, Winged Wisdom (Modern Horizons 3,
/// {G}{W}{U}).
///
/// ## Card text
/// - Legendary Creature — Bird Bard 3/4.
/// - Flying.
/// - "Whenever a creature you control becomes the target of a spell or
///    ability, that creature's controller may reveal the top card of their
///    library. If a land card is revealed this way, that player puts it
///    onto the battlefield. Otherwise, they put that card into their hand.
///    This ability triggers only twice each turn."
///
/// ## Implemented (v1)
/// - 3/4 Legendary Creature with Bird + Bard subtypes and Flying (CR
///   702.9 — <see cref="KeywordAbility"/>).
/// - Targeted-by-spell-or-ability trigger (CR 603.6c, 115.6) wired via
///   <see cref="TargetsChosenEvent"/>. Predicate matches any chosen target
///   that is a creature controlled by Nadu's controller (covers both the
///   spell + activated/triggered ability sources, since the engine
///   publishes <see cref="TargetsChosenEvent"/> from both
///   <see cref="Majik.Core.Services.SpellCaster"/> and
///   <see cref="Majik.Core.Services.AbilityActivator"/>).
/// - Per-turn cap: the trigger only fires for the first two events each
///   turn (CR 603.2 / 603.3 — "this ability triggers only twice each
///   turn"). A shared closure counts surfaced triggers and a
///   <see cref="TurnStartedEvent"/> handler resets the counter at the
///   start of each new turn (CR 500.1). Mirrors the per-turn-counter
///   pattern in <see cref="LedgerShredderFactory"/>.
/// - On resolution: reveal the top of the targeted creature's
///   controller's library; if it's a land card, put it onto the
///   battlefield under that player's control, otherwise put it into their
///   hand. The "may" is auto-taken in v1 (no agent prompt) — same
///   simplification as Tireless Tracker's Clue trigger.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent "may" prompt</b>: the optional reveal is currently always
///   taken. An <see cref="Majik.Core.Players.IPlayerAgent"/> hook (e.g.
///   <c>ChooseMayRevealAsync</c>) would let the controller decline the
///   reveal — keeping this auto-take aligns with how other "may" riders
///   resolve in the engine today.
/// - <b>ETB triggers on the revealed land</b>: when the revealed card is
///   a land, the v1 implementation moves it via
///   <see cref="Player.Zones"/> directly (Library → Battlefield) and sets
///   the card's zone + controller. Production paths should route through
///   <see cref="Majik.Core.Services.ZoneService"/> when available so
///   ETB-replacement effects (CR 614) and zone-change triggers fire; the
///   live overload accepts an optional zone service for that wiring.
/// - <b>"That creature's controller"</b>: v1 reads
///   <see cref="ICard.Controller"/> on the targeted creature at trigger
///   evaluation time. Reading at resolution time (CR 603.2c — most
///   triggers check controller on resolution) is not modelled because
///   <see cref="TargetsChosenEvent"/> is the only attachment point.
/// </summary>
[CardName("Nadu, Winged Wisdom")]
public static class NaduWingedWisdomFactory
{
    /// <summary>
    /// Maximum number of times Nadu's reveal trigger may fire each turn
    /// (printed text — "This ability triggers only twice each turn").
    /// </summary>
    public const int MaxTriggersPerTurn = 2;

    /// <summary>
    /// Construct Nadu, Winged Wisdom with no live event-bus / trigger-
    /// manager wiring. The reveal trigger is attached to the card so
    /// structural / dispatch tests see the ability shape, but no
    /// <see cref="TurnStartedEvent"/> reset handler is installed; tests
    /// drive turn-boundary resets via the live overload.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Nadu, Winged Wisdom with optional event bus + trigger
    /// manager. When <paramref name="eventBus"/> is supplied, a
    /// <see cref="TurnStartedEvent"/> handler resets the twice-per-turn
    /// counter. When <paramref name="triggers"/> is supplied, the
    /// targeted-by trigger is registered so a
    /// <see cref="TargetsChosenEvent"/> matching a creature Nadu's
    /// controller controls automatically surfaces as pending — capped at
    /// two per turn.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Nadu, Winged Wisdom",
            manaCost: "{G}{W}{U}",
            power: 3,
            toughness: 4,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Bird, CardSubtype.Bard });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.9 — Flying.
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Per-turn trigger counter. Shared between the trigger predicate
        // (which increments on each successful match) and the
        // TurnStartedEvent reset handler. We count surfaced triggers, not
        // events evaluated — so events that don't match (e.g. a spell
        // targeting a creature Nadu's controller doesn't control) don't
        // consume the per-turn budget. CR 603.2 / 603.3.
        // ----------------------------------------------------------------
        var triggersThisTurn = new int[] { 0 };

        // Capture the targeted creature's controller at trigger-evaluation
        // time so the resolution effect knows which player reveals.
        Player? capturedRevealer = null;

        // ----------------------------------------------------------------
        // Targeted-by-spell-or-ability trigger — CR 603.6c, 115.6.
        //
        // Fires on TargetsChosenEvent where ANY chosen target is a
        // creature controlled by Nadu's controller AND we haven't yet
        // surfaced two triggers this turn.
        //
        // The event is published by both SpellCaster and AbilityActivator,
        // so "spell or ability" is covered automatically (mirrors
        // Phantasmal Image's spell-or-ability self-sac trigger).
        // ----------------------------------------------------------------
        var condition = new EventTriggerCondition<TargetsChosenEvent>((e, _) =>
        {
            // Twice-per-turn cap (CR 603.2 / 603.3).
            if (triggersThisTurn[0] >= MaxTriggersPerTurn) return false;

            // Find the first chosen target that is a creature controlled
            // by Nadu's controller. We use Nadu's controller (not Nadu's
            // owner) so a control-changing effect on Nadu would route the
            // trigger correctly if/when that pathway lands.
            foreach (var t in e.Targets)
            {
                if (t.TargetType != TargetType.Permanent && t.TargetType != TargetType.Card)
                {
                    continue;
                }
                if (t is not Target concrete) continue;
                if (concrete.TargetObject is not ICard targetCard) continue;
                if (!targetCard.HasType(CardType.Creature)) continue;
                if (!ReferenceEquals(targetCard.Controller, card.Controller)) continue;

                capturedRevealer = targetCard.Controller;
                triggersThisTurn[0]++;
                return true;
            }

            return false;
        });

        var revealEffect = new Effect(
            "Nadu, Winged Wisdom: that creature's controller reveals top of library; " +
            "land → battlefield, otherwise → hand",
            () =>
            {
                var revealer = capturedRevealer;
                capturedRevealer = null;
                if (revealer == null) return;

                var library = revealer.Zones.Library;
                var top = library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — no-op

                library.RemoveCard(top);

                if (top.HasType(CardType.Land))
                {
                    // CR 305.1 — putting a land onto the battlefield this
                    // way does NOT count as a land drop for the turn (the
                    // effect doesn't say "play"). Direct zone wiring; route
                    // through ZoneService in fully-wired callers to get
                    // ETB/replacement effects.
                    revealer.Zones.Battlefield.AddCard(top);
                    top.SetZone(ZoneType.Battlefield);
                    top.SetController(revealer);
                }
                else
                {
                    revealer.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { revealEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);

        // CR 500.1 — reset the per-turn counter when a new turn starts.
        if (eventBus != null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => triggersThisTurn[0] = 0);
        }

        // Live registration with TriggerManager so the bus actually
        // surfaces the trigger as pending when a spell/ability targets a
        // creature Nadu's controller controls.
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
