using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Mentor of the Meek (Innistrad, {2}{W}).
///
/// Creature — Human Soldier 2/2. Oracle text:
///   "Whenever another creature with power 2 or less enters under your
///    control, you may pay {1}. If you do, draw a card."
///
/// White-weenie / Soul-Sisters / tokens value engine — every small
/// creature ETB under your control banks an optional cantrip for {1}.
/// Pairs with Soul Warden / Champion of the Parish / token producers
/// (Bitterblossom, Lingering Souls), all of which print 1/1s that
/// satisfy the "power 2 or less" gate.
///
/// ## Implemented (v1)
///
/// - 2/2 <see cref="Creature"/> — Human Soldier, mana cost {2}{W}.
///   Owner / controller wired.
/// - <b>Triggered ability (CR 603.1 / CR 603.6a)</b>:
///   "Whenever another creature with power 2 or less enters under your
///    control, you may pay {1}. If you do, draw a card." Subscribes to
///   <see cref="CardMovedEvent"/> with a custom predicate that gates
///   on (a) target zone = battlefield, (b) entering card is a
///   <see cref="Creature"/>, (c) controller is Mentor's controller,
///   (d) entering card is NOT Mentor (CR 109.5 — "another"), and
///   (e) <see cref="Creature.BasePower"/> ≤ 2 (same printed-P/T read
///   as <see cref="GuideOfSoulsFactory"/>'s ETB predicate).
/// - <b>"You may pay {1}" optional rider (CR 117.5)</b>: consults the
///   controller's <see cref="IPlayerAgent"/> via
///   <see cref="IPlayerAgent.ChooseYesNoAsync"/>. Agent-less callers
///   auto-pay if able (Animation Module / Lightning Rift posture).
///   <see cref="Player.PayMana"/> returns false when the pool can't
///   satisfy {1}; the trigger fizzles harmlessly.
/// - <b>Draw a card</b>: top of controller's library to hand. Empty
///   library flags the SBA loss (CR 704.5b / CR 120.3) via
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> — same
///   posture as Faithless Looting / Cling to Dust.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — shape only. Trigger attached for
///   structural / dispatcher inspection; not registered with any
///   <see cref="TriggerManager"/>.
/// - <see cref="Create(Player, TriggerManager?)"/> — fully wired. The
///   trigger registers for bus-driven firing.
///
/// ## Notes
///
/// - <b>Self-trigger</b>: Mentor entering does NOT trigger itself —
///   the predicate's <c>!ReferenceEquals(e.Card, card)</c> guard
///   matches the printed "another creature" wording (CR 109.5).
/// - <b>BasePower read</b>: the predicate reads
///   <see cref="Creature.BasePower"/> at the moment of ETB, mirroring
///   Guide of Souls' "power 2 or less" gate. Anthems / pump effects in
///   Layer 7c don't affect the printed base power — a 1/1 token under
///   Glorious Anthem still reads BasePower = 1 here, which matches the
///   printed Mentor's intent (the "power 2 or less" clause is read at
///   ETB time per CR 603.6d, before pump anthems are recomputed). The
///   one lossy case is "ETBs as a 3/3 token that was supposed to print
///   as a 1/1" — no such printed card exists.
/// </summary>
[CardName("Mentor of the Meek")]
public static class MentorOfTheMeekFactory
{
    public const string CardName = "Mentor of the Meek";
    public const string PrintedManaCost = "{2}{W}";
    public const int Power = 2;
    public const int Toughness = 2;
    public const int MaxTriggeringPower = 2;
    public const int OptionalManaCost = 1;

    /// <summary>
    /// Construct Mentor of the Meek with no live wiring. The cantrip
    /// trigger is attached to the card shape for dispatcher / structural
    /// tests; not registered with any <see cref="TriggerManager"/>.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null);

    /// <summary>
    /// Construct Mentor of the Meek with optional <see cref="TriggerManager"/>
    /// wiring. When supplied, the cantrip trigger registers so a
    /// matching <see cref="CardMovedEvent"/> (creature → battlefield
    /// under Mentor's controller, BasePower ≤ 2, not Mentor itself)
    /// automatically queues the may-pay-then-draw effect.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Soldier });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Triggered ability — CR 603.1 / CR 603.6a.
        //   "Whenever another creature with power 2 or less enters under
        //    your control, you may pay {1}. If you do, draw a card."
        //
        // Predicate gates on (a) entering the battlefield, (b) card type
        // Creature, (c) controller match, (d) not Mentor itself (CR 109.5
        // — "another"), (e) BasePower ≤ 2 (printed P/T read; same posture
        // as GuideOfSoulsFactory's "power 2 or less" ETB predicate).
        // ----------------------------------------------------------------
        var etbCondition = new EventTriggerCondition<CardMovedEvent>(
            (e, _) =>
            {
                if (e.ToZone != ZoneType.Battlefield) return false;
                if (!e.Card.HasType(CardType.Creature)) return false;
                if (!ReferenceEquals(e.Card.Controller, card.Controller ?? owner)) return false;
                if (ReferenceEquals(e.Card, card)) return false;
                if (e.Card is not Creature entering) return false;
                return entering.BasePower <= MaxTriggeringPower;
            });

        var triggerEffect = new Effect(
            $"{CardName}: may pay {{{OptionalManaCost}}} → draw a card",
            async ctx =>
            {
                // CR 603.6c — Mentor must still be on the battlefield to
                // fire. activeZones gates the event match; the in-effect
                // check is defence-in-depth for manual Execute() calls.
                if (card.Zone != ZoneType.Battlefield) return;

                var triggerController = card.Controller ?? owner;

                // "You may pay {1}" — consult controller's agent.
                // Agent-less fallback: auto-pay if able (Animation
                // Module / Lightning Rift posture).
                var oneGeneric = ManaCost.Zero.AddGenericCost(OptionalManaCost);
                var agent = ctx.Agent ?? AgentRegistry.Get(triggerController);
                bool pay;
                if (agent != null)
                {
                    pay = (await agent.ChooseYesNoAsync(
                        $"Pay {{{OptionalManaCost}}} to draw a card?",
                        BotIntent.Draw).ConfigureAwait(false));
                }
                else
                {
                    pay = true;
                }

                if (!pay) return;

                // CR 117.5 — optional may-pay; trigger fizzles when the
                // mana isn't available.
                if (!triggerController.PayMana(oneGeneric)) return;

                // CR 121.1 — draw a card. Top of library → hand; empty
                // library flags SBA loss (CR 704.5b / CR 120.3). Same
                // posture as Faithless Looting / Cling to Dust.
                var top = triggerController.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    triggerController.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }
                triggerController.Zones.Library.RemoveCard(top);
                triggerController.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: etbCondition,
            effects: new IEffect[] { triggerEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);

        return card;
    }
}
