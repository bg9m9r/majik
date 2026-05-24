using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Esper Sentinel (Modern Horizons 2, {W}).
///
/// Creature — Human Soldier 1/1. Oracle text:
///   "Whenever an opponent casts their first noncreature spell each turn,
///    unless they pay {X}, where X is the number of creatures you control,
///    you draw a card."
///
/// ## Implemented (v1)
/// - 1/1 Creature — Human Soldier, mana cost {W}.
/// - <b>Per-opponent first-noncreature trigger (CR 603.1)</b> over
///   <see cref="SpellCastEvent"/>:
///     * Caster is NOT the Sentinel's controller (an opponent).
///     * Spell's <see cref="ICard"/> does NOT have
///       <see cref="CardType.Creature"/>.
///     * It is that caster's first qualifying spell THIS TURN (per-player
///       closure keyed by player ID).
/// - <b>Per-turn closure reset</b> via an optional <see cref="IEventBus"/>:
///   on each <see cref="TurnStartedEvent"/> the per-player count map is
///   cleared (CR 500.1).
/// - <b>Pay {X} or controller draws</b>: on resolution, X = the controller's
///   creature count on the battlefield (recomputed at resolve, CR 608.2).
///   The opponent who cast the spell is asked to pay {X}; v1 auto-pays
///   from the opponent's <see cref="Player.ManaPool"/> when possible
///   (mirrors Cursecatcher / Daze / Mana Leak — same "auto-consults the
///   mana pool" posture, no agent Yes/No prompt yet). If payment succeeds,
///   no draw. If payment is impossible (X = 0 is treated as auto-paid —
///   CR 117.5: paying {0} succeeds trivially) or the opponent's pool can't
///   cover the cost, the Sentinel's controller draws 1 card via
///   <see cref="Fx.DrawCards"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Agent "would you like to pay {X}?" Yes/No surface</b>: no
///   <c>ChooseYesNoAsync</c> on <see cref="Players.Agents.IPlayerAgent"/>
///   yet. v1 auto-pays from pool when affordable, otherwise declines.
///   Same gap as Daze / Mana Leak / Cursecatcher's "unless pay" clause.
/// - <b>Caster's pre-cast mana intent</b>: opponents won't routinely have
///   leftover mana sitting in pool; the "auto-pay" path is a structural
///   placeholder that satisfies the rules-resolve shape but only fires
///   when bots / scripts have explicitly added mana for the tax. Once
///   <c>ChooseYesNoAsync</c> ships, the resolve body becomes "prompt → if
///   yes, pay {X}; else draw."
/// </summary>
[CardName("Esper Sentinel")]
public static class EsperSentinelFactory
{
    public const string CardName = "Esper Sentinel";
    public const string PrintedManaCost = "{W}";

    /// <summary>
    /// Construct Esper Sentinel with no live bus / trigger-manager wiring.
    /// The trigger is attached to the card so structural / dispatcher tests
    /// see its shape; the per-turn closure is never reset (callers exercising
    /// the trigger manually can reset by constructing a fresh card or by
    /// invoking the (owner, bus, triggers) overload).
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Esper Sentinel with optional event bus + trigger manager.
    /// When <paramref name="eventBus"/> is supplied a
    /// <see cref="TurnStartedEvent"/> subscription resets the per-player
    /// first-cast map (CR 500.1). When <paramref name="triggers"/> is
    /// supplied the trigger is registered so a qualifying
    /// <see cref="SpellCastEvent"/> automatically places it on the stack.
    /// </summary>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: 1,
            toughness: 1,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Per-turn, per-opponent "noncreature casts so far" map. Keyed by
        // Player reference so each opponent gets their own first-cast
        // count (the trigger fires on each opponent's FIRST qualifying
        // spell each turn — CR 603.1).
        // ----------------------------------------------------------------
        var castsThisTurn = new Dictionary<Player, int>();

        // The spell whose cast caused the trigger to fire — captured by
        // the predicate so the resolve body knows which opponent to ask
        // for {X}. Reset to null between trigger firings; the resolve
        // body re-reads it under the assumption that triggers resolve
        // before the next SpellCastEvent (one-spell-on-the-stack invariant
        // at trigger-fire time, CR 116.5).
        var pendingCaster = new Player?[] { null };

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
        {
            var caster = e.Spell.Controller;
            if (caster is null) return false;
            if (ReferenceEquals(caster, owner)) return false;             // controller's own spells skip
            if (e.Spell.Card.HasType(CardType.Creature)) return false;    // creature spells skip

            // Increment + only fire on the exact transition to 1.
            if (!castsThisTurn.TryGetValue(caster, out var n)) n = 0;
            n++;
            castsThisTurn[caster] = n;
            if (n != 1) return false;

            pendingCaster[0] = caster;
            return true;
        });

        var taxEffect = new Effect(
            $"{CardName}: opponent pays {{X}} (X = creatures you control) or you draw a card",
            () =>
            {
                var caster = pendingCaster[0];
                pendingCaster[0] = null;
                if (caster is null) return;

                // CR 608.2 — recompute X at resolve time. X = number of
                // creatures the Sentinel's controller controls (Sentinel
                // itself counts when on the battlefield).
                var x = owner.Zones.Battlefield.GetCards()
                    .Count(c => c.HasType(CardType.Creature)
                                && ReferenceEquals(c.Controller, owner));

                // CR 117.5 — paying {0} succeeds trivially with no mana
                // change. The Sentinel's draw is then suppressed.
                if (x <= 0)
                {
                    return;
                }

                // v1 auto-pay from the opponent's mana pool. Matches the
                // Cursecatcher / Daze / Mana Leak posture: if the mana is
                // sitting in the pool, pay it; otherwise decline → draw.
                if (caster.PayMana(ManaCost.Zero.AddGenericCost(x)))
                {
                    return;
                }

                // Opponent declined / can't pay → controller draws 1.
                Fx.DrawCards(owner, 1);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { taxEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        // CR 500.1 — reset the per-player count map at the start of each
        // new turn so the "first noncreature spell each turn" wording is
        // honoured per opponent.
        if (eventBus is not null)
        {
            eventBus.Subscribe<TurnStartedEvent>(_ => castsThisTurn.Clear());
        }

        return card;
    }
}
