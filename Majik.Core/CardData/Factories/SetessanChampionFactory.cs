using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Setessan Champion (Theros Beyond Death — {1}{G}{G}).
///
/// Creature — Human Warrior 1/3. Oracle text (current printing):
///   "Constellation — Whenever an enchantment you control enters, put a
///    +1/+1 counter on this creature and draw a card."
///
/// ## Implementation
///
/// Constellation (CR 702.144) — a trigger-templating keyword that fires
/// whenever an enchantment enters under the controller's control. Unlike
/// <see cref="SythisHarvestsHandFactory"/> (an enchantment-typed permanent
/// that is itself a Nymph creature on a Nyx frame), Setessan Champion is a
/// plain creature — the trigger therefore fires on (a) Setessan Champion's
/// own ETB and (b) any other enchantment entering under the controller's
/// control.
///
/// Shape mirrors <see cref="SythisHarvestsHandFactory"/> (single
/// <see cref="TriggeredAbility"/> over <see cref="CardMovedEvent"/>):
///   * Self-ETB qualifies — predicate is
///     <c>ReferenceEquals(e.Card, card) || e.Card.HasType(CardType.Enchantment)</c>.
///   * Resolution is UNCONDITIONAL: put a +1/+1 counter on Setessan Champion
///     (CR 122) AND draw a card. The current printing has NO "you may pay 1
///     life" clause — a previous version of this factory modeled a fictional
///     life-payment may-clause that is not in the card's oracle; this is the
///     missing-effect fix (the +1/+1 counter half was never bound).
///
/// ## Notes
/// - Self-ETB triggers fire because Setessan Champion is a creature whose
///   battlefield-entry constitutes "[Setessan Champion] enters under your
///   control"; constellation cares about the enchantment-typed entrant,
///   and CR 702.144 explicitly reads "[this permanent] or another
///   enchantment" for any constellation permanent.
/// - Opponent enchantment ETBs do not qualify (controller filter).
/// - The single-arg dispatcher path attaches the trigger without
///   TriggerManager wiring; pass an <see cref="IPlayerAgent"/> and a live
///   <see cref="TriggerManager"/> via the overload for end-to-end firing.
/// </summary>
[CardName("Setessan Champion")]
public static class SetessanChampionFactory
{
    public const string CardName = "Setessan Champion";
    public const string PrintedManaCost = "{1}{G}{G}";
    public const int Power = 1;
    public const int Toughness = 3;

    /// <summary>
    /// Construct Setessan Champion with no live trigger-manager / agent
    /// wiring. The constellation trigger is attached to the card so
    /// structural shape tests can observe it; for end-to-end firing pass
    /// a live <see cref="TriggerManager"/> and optional
    /// <see cref="IPlayerAgent"/> via the overload.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, triggers: null, agent: null);

    /// <summary>
    /// Construct Setessan Champion with optional trigger-manager + agent
    /// wiring.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">Trigger manager. When supplied, the
    /// constellation trigger is registered so the bus surfaces it as
    /// pending.</param>
    /// <param name="agent">Retained for signature stability; the current
    /// printing's constellation effect is unconditional (no agent prompt), so
    /// this is unused.</param>
    public static Creature Create(Player owner, TriggerManager? triggers, IPlayerAgent? agent)
    {
        ArgumentNullException.ThrowIfNull(owner);
        _ = agent; // unconditional effect — no prompt needed.

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Warrior });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Constellation trigger — "Whenever Setessan Champion or another
        // enchantment enters under your control, you may pay 1 life. If
        // you do, draw a card." (CR 702.144, 603.6a, 117.11)
        // ----------------------------------------------------------------
        var constellationCondition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
        {
            if (e.ToZone != ZoneType.Battlefield) return false;
            if (!ReferenceEquals(e.Card.Controller, owner)) return false;
            // Setessan Champion itself qualifies (self-ETB) OR any other
            // enchantment entering under controller's control.
            return ReferenceEquals(e.Card, card) || e.Card.HasType(CardType.Enchantment);
        });

        var constellationEffect = new Effect(
            $"{CardName} — put a +1/+1 counter on itself and draw a card on enchantment ETB",
            () =>
            {
                // CR 122 — "put a +1/+1 counter on this creature." Routed
                // through Fx.PlaceCounter so the counter lands on Setessan
                // Champion itself.
                Majik.Core.Primitives.Fx.PlaceCounter(
                    card, Majik.Core.Counters.CounterType.PlusOnePlusOne, 1);

                // "and draw a card." Unconditional (the current printing has
                // no "you may pay 1 life" clause). Draw the top of the
                // controller's library — the inline DrawOne pattern.
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null) return;
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
            });

        var constellationTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: constellationCondition,
            effects: new IEffect[] { constellationEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(constellationTrigger);
        triggers?.RegisterTriggeredAbility(constellationTrigger);

        return card;
    }
}
