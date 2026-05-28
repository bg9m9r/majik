using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Coiling Oracle (Ravnica: City of Guilds / Modern
/// reprints, {G}{U}).
///
/// Creature — Snake Elf Druid 1/1. Oracle text:
///   "When this creature enters, reveal the top card of your library. If it's
///    a land card, put it onto the battlefield. Otherwise, put that card into
///    your hand."
///
/// ## Implementation
///
/// - 1/1 Creature — Snake Elf Druid ({G}{U}). Color identity green + blue
///   (derived from both pips per CR 202.2c). Mana value 2 (CR 202.3).
/// - <b>ETB triggered ability (CR 603.1 / CR 603.6a)</b>: self-ETB via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>. No intervening-if clause
///   (CR 603.4 does not apply — the oracle text has no "if" at trigger time).
/// - On resolution:
///   1. Peek the top of the controller's library. Empty library → no-op
///      (no reveal, no zone move). CR 701.16 — nothing to reveal.
///   2. If it's a land card (CR 305.1, <c>HasType(CardType.Land)</c>):
///      put it onto the battlefield under the controller's control.
///      NOTE: this does NOT count as a land drop (CR 305.2 — the oracle text
///      says "put", not "play"). The land enters untapped per CR 303.4
///      (nothing in the text says "tapped").
///   3. Otherwise: put that card into the controller's hand.
/// - Zone moves use raw-zone manipulation in the shape-only path; the fully
///   wired overload should route through
///   <see cref="Majik.Core.Services.ZoneService"/> when available so
///   ETB-replacement effects (CR 614) and zone-change triggers fire
///   (same gap as NaduWingedWisdomFactory v1 and MatterReshaperFactory).
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — shape only. ETB trigger is attached to
///   the card for shape inspection; not registered with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / structural
///   tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. ETB trigger registered with <paramref name="triggers"/> so
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s on the
///   bus route it to the stack. Reveal publishes a
///   <see cref="CardRevealedEvent"/> when an <paramref name="eventBus"/>
///   is supplied.
///
/// ## Design notes
/// The reveal-then-branch pattern is structurally identical to Nadu, Winged
/// Wisdom's per-trigger effect body (NaduWingedWisdomFactory): peek
/// <c>library.GetCards().FirstOrDefault()</c>, remove from library, branch
/// on <c>HasType(CardType.Land)</c>, place in battlefield or hand, stamp
/// zone + controller. The ETB trigger wiring follows CloudkinSeerFactory
/// (dual Create overloads, <c>Triggers.OnEnterBattlefieldSelf</c>,
/// <c>activeZones = { Battlefield }</c>).
///
/// The controller closure re-resolves at execute time via
/// <c>card.Controller ?? owner</c> so blink / control-change scenarios
/// reveal for the correct player (same as CloudkinSeerFactory's draw
/// closure).
///
/// ## Deferred (v1 gaps)
/// - <b>Agent "may" prompt</b>: the printed oracle text does not say "may
///   reveal" — the reveal is mandatory. No prompt needed here; v1 always
///   reveals and routes correctly.
/// - <b>ETB triggers on the revealed land</b>: v1 raw-zone path skips the
///   ZoneService call; future wiring should pass ZoneService for Containment
///   Priest / Soul Warden ETB routing (same gap as NaduWingedWisdomFactory).
/// </summary>
[CardName("Coiling Oracle")]
public static class CoilingOracleFactory
{
    public const string CardName = "Coiling Oracle";
    public const string PrintedManaCost = "{G}{U}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>
    /// Construct Coiling Oracle with no live wiring. The ETB trigger is
    /// attached to the card for shape inspection; not registered with any
    /// <see cref="TriggerManager"/>. Suitable for dispatcher / structural
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Coiling Oracle with optional runtime services. When
    /// <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>s
    /// published on the bus route it to the stack. When
    /// <paramref name="eventBus"/> is supplied, the reveal step publishes a
    /// <see cref="CardRevealedEvent"/> so portal / log subscribers can
    /// flash the card.
    /// </summary>
    public static Creature Create(
        Player owner,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Snake, CardSubtype.Elf, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability (CR 603.1 / CR 603.6a).
        //   "When this creature enters, reveal the top card of your library.
        //    If it's a land card, put it onto the battlefield. Otherwise,
        //    put that card into your hand."
        //
        // Unconditional self-ETB via Triggers.OnEnterBattlefieldSelf —
        // no intervening-if (CR 603.4 does not apply). ActiveZones =
        // { Battlefield } (CR 603.6a — triggers require the source to be
        // on the battlefield at the time of trigger).
        //
        // Controller closure re-resolves at execute time so blink /
        // control-change scenarios reveal for the correct player.
        // ----------------------------------------------------------------
        var etbCondition = Triggers.OnEnterBattlefieldSelf(card);

        var etbEffect = new Effect(
            $"{CardName}: reveal top of library; land → battlefield, else → hand",
            () =>
            {
                var controller = card.Controller ?? owner;
                var library = controller.Zones.Library;
                var top = library.GetCards().FirstOrDefault();
                if (top == null) return; // empty library — no-op (CR 701.16)

                // CR 701.16 — public reveal. Publish event when bus is
                // available so portal / log subscribers can flash the card.
                eventBus?.Publish(new CardRevealedEvent(
                    top, controller, ZoneType.Library, CardName));

                library.RemoveCard(top);

                if (top.HasType(CardType.Land))
                {
                    // CR 305.1 — putting a land onto the battlefield this
                    // way does NOT count as a land drop (CR 305.2 — "put",
                    // not "play"). Land enters untapped (no text qualifier).
                    // Raw-zone wiring; route through ZoneService in
                    // fully-wired callers to get ETB / replacement effects.
                    controller.Zones.Battlefield.AddCard(top);
                    top.SetZone(ZoneType.Battlefield);
                    if (top is Permanent perm)
                    {
                        perm.SetController(controller);
                        perm.MarkEnteredBattlefield();
                    }
                    else
                    {
                        top.SetController(controller);
                    }
                }
                else
                {
                    // Nonland — put it into the controller's hand.
                    controller.Zones.Hand.AddCard(top);
                    top.SetZone(ZoneType.Hand);
                }
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { etbEffect },
            interveningIf: null,
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
