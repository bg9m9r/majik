using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wick, the Whorled Mind (Duskmourn: House of Horror,
/// {3}{B}).
///
/// Legendary Creature — Rat Warlock 2/4. Oracle text (current Scryfall):
///   "Whenever Wick or another Rat you control enters, create a 1/1 black
///    Snail creature token if you don't control a Snail. Otherwise, put a
///    +1/+1 counter on a Snail you control.
///    {U}{B}{R}, Sacrifice a Snail: Wick deals damage equal to the sacrificed
///    creature's power to each opponent. Then draw cards equal to the
///    sacrificed creature's power."
///
/// ## Implemented (v1)
///
/// - <b>Legendary Rat Warlock 2/4</b> at {3}{B} (CR 205.3 subtypes,
///   CR 205.4 Legendary supertype). The base <see cref="Creature"/>
///   constructor takes the supertype + subtypes directly.
///
/// - <b>Self-or-another-Rat ETB trigger (CR 603.6e)</b>: wired via
///   <see cref="EventTriggerCondition{T}"/> over <see cref="CardMovedEvent"/>
///   filtered to:
///     1. <c>ToZone == Battlefield</c>.
///     2. The entering card is a Rat (<see cref="CardSubtype.Rat"/>) — covers
///        BOTH Wick's own entry (Wick is itself a Rat, satisfying the "Wick
///        or another Rat" clause without a separate self branch) and any other
///        Rat entering.
///     3. The entering card's controller equals Wick's controller ("Rat YOU
///        control", CR 109.5).
///   On resolution the conditional payoff (CR 603 / 111 / 122.1c):
///     - If the controller does NOT control a Snail → create one 1/1 black
///       Snail creature token (CR 111). Routed through
///       <see cref="TokenFactory.CreateOnBattlefield"/> with the supplied
///       <see cref="ZoneService"/> (so token-ETB observers fire); falls back
///       to a raw battlefield placement on the shape-only path.
///     - Otherwise → put a +1/+1 counter on a Snail the controller controls
///       (CR 122.1c), routed through <see cref="CountersService.Add"/> so
///       Hardened Scales / Doubling Season replacements observe it (CR 614).
///       The choice of which Snail (when several) is the first controlled
///       Snail — a deterministic v1 pick matching the sibling
///       "put a counter on a &lt;your&gt; permanent" effects.
///
/// - <b>{U}{B}{R}, Sacrifice a Snail: deal damage = sac power to each
///   opponent, then draw that many (CR 117 / 601.2f / 119 / 120)</b>: an
///   <see cref="ActivatedAbility"/> whose costs are a {U}{B}{R}
///   <see cref="ManaCostCost"/> plus a
///   <see cref="SacrificeFilteredCost.ForSubtype"/>(Snail). The sacrificed
///   Snail is exposed via <see cref="SacrificeFilteredCost.Target"/> after the
///   cost is paid (same "sacrifice as a cost, then read the sacrificed
///   permanent's power" idiom as Kazuul's Fury / Fling). On resolution:
///     - Each opponent (read from the live
///       <see cref="Majik.Core.Game.GameContext.Opponents"/>, CR 102.1) takes
///       damage equal to the sacrificed creature's power (CR 119.3 — a
///       zero-power sacrifice deals 0, dealing nothing).
///     - The controller draws that many cards (CR 120) via
///       <see cref="Fx.DrawCards"/>.
///   The sacrificed creature's power is captured at cost-payment time
///   (CR 608.2g — last-known information for the sacrificed permanent); a
///   vanilla Snail token's <see cref="Creature.Power"/> equals its base power.
///
/// Adding this <c>[CardName]</c> factory flips <c>IsImplemented</c> on
/// automatically via <see cref="ImplementedCardNames"/> — no seed regen.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The ETB trigger + the
///   activated ability are attached so structural / dispatcher tests observe
///   them; token creation falls back to a raw zone move (no token-ETB events)
///   and the trigger is NOT registered with a live TriggerManager.
/// - <see cref="Create(Player, ZoneService?, TriggerManager?, ReplacementBus?)"/>
///   — fully wired. ETB trigger registered when <paramref name="triggers"/> is
///   supplied; token creation publishes <see cref="CardMovedEvent"/> when a
///   <paramref name="zones"/> service is supplied; the +1/+1 counter is routed
///   through <paramref name="replacements"/> when supplied (CR 614).
/// </summary>
[CardName("Wick, the Whorled Mind")]
public static class WickTheWhorledMindFactory
{
    public const string CardName = "Wick, the Whorled Mind";
    public const string PrintedManaCost = "{3}{B}";
    public const int Power = 2;
    public const int Toughness = 4;

    /// <summary>CR 601.2f — the activated ability's mana cost.</summary>
    public const string ActivationManaCost = "{U}{B}{R}";

    /// <summary>1/1 black Snail token spec (CR 111.4).</summary>
    public const int SnailTokenPower = 1;
    public const int SnailTokenToughness = 1;

    public static Creature Create(Player owner) =>
        Create(owner, zones: null, triggers: null, replacements: null);

    /// <summary>
    /// Construct Wick with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="zones">ZoneService for token creation so a created Snail's
    /// ETB <see cref="CardMovedEvent"/> fires (Soul Warden etc.). May be null —
    /// the token is placed via a raw battlefield move on the shape-only
    /// path.</param>
    /// <param name="triggers">TriggerManager for the Rat-ETB trigger. May be
    /// null — the trigger is still attached to the card shape.</param>
    /// <param name="replacements">ReplacementBus for routing the +1/+1 counter
    /// placement through <see cref="CountersService.Add"/> (CR 614). May be
    /// null — the counter is placed directly.</param>
    public static Creature Create(
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        ReplacementBus? replacements = null)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Rat, CardSubtype.Warlock });

        card.SetOwner(owner);
        card.SetController(owner);

        AttachEtbTrigger(card, owner, zones, triggers, replacements);
        AttachSacrificeActivatedAbility(card, owner);

        return card;
    }

    // -------------------------------------------------------------------------
    // ETB trigger — CR 603.6e ("Wick or another Rat you control enters").
    // -------------------------------------------------------------------------
    private static void AttachEtbTrigger(
        Creature card,
        Player owner,
        ZoneService? zones,
        TriggerManager? triggers,
        ReplacementBus? replacements)
    {
        // Predicate: a Rat the same player controls entering the battlefield.
        // Wick itself is a Rat, so its own ETB satisfies the "Wick or another
        // Rat" clause (CR 603.6e) without a separate self branch.
        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card is Creature entering
            && entering.HasSubtype(CardSubtype.Rat)
            && ReferenceEquals(e.Card.Controller, card.Controller ?? owner));

        var effect = new Effect(
            $"{CardName}: create a Snail token, or +1/+1 a Snail you control",
            () =>
            {
                var controller = card.Controller ?? owner;

                var snail = FirstControlledSnail(controller);
                if (snail is null)
                {
                    // CR 111 — "create a 1/1 black Snail creature token if you
                    // don't control a Snail."
                    CreateSnailToken(controller, zones);
                }
                else
                {
                    // CR 122.1c — "Otherwise, put a +1/+1 counter on a Snail you
                    // control." Routed through CountersService so Hardened
                    // Scales / Doubling Season replacements observe it (CR 614).
                    CountersService.Add(snail, CounterType.PlusOnePlusOne, 1, replacements);
                }
            });

        var trigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: condition,
            effects: new IEffect[] { effect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(trigger);
        triggers?.RegisterTriggeredAbility(trigger);
    }

    // -------------------------------------------------------------------------
    // Activated ability — {U}{B}{R}, Sacrifice a Snail (CR 601.2f / 119 / 120).
    // -------------------------------------------------------------------------
    private static void AttachSacrificeActivatedAbility(Creature card, Player owner)
    {
        // The sacrifice-a-Snail cost exposes the sacrificed permanent via its
        // Target after Pay succeeds, so the resolution effect can read the
        // sacrificed creature's power (CR 608.2g — last-known info). Same
        // captured-cost idiom as Kazuul's Fury's SacrificeCreatureCost.
        var sacrificeCost = SacrificeFilteredCost.ForSubtype(CardSubtype.Snail);
        var manaCost = new ManaCostCost(ManaCost.Parse(ActivationManaCost));

        var effect = new Effect(
            $"{CardName}: deal sac power to each opponent, then draw that many",
            rc =>
            {
                var controller = rc.Controller;

                // CR 608.2g — the sacrificed Snail's power, captured when the
                // cost was paid. Null Target / no game context → nothing to do.
                int power = sacrificeCost.Target is Creature sacrificed
                    ? sacrificed.Power
                    : 0;

                if (power > 0 && rc.Game is not null)
                {
                    // CR 102.1 / 119.3 — "deals damage equal to the sacrificed
                    // creature's power to each opponent."
                    foreach (var opponent in rc.Game.Opponents)
                    {
                        Fx.DealDamageAny(opponent, power, card);
                    }
                }

                if (power > 0)
                {
                    // CR 120 — "Then draw cards equal to the sacrificed
                    // creature's power."
                    Fx.DrawCards(controller, power);
                }

                return ValueTask.CompletedTask;
            });

        var ability = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[] { manaCost, sacrificeCost },
            effects: new IEffect[] { effect });

        card.AddAbility(ability);
    }

    private static Creature? FirstControlledSnail(Player controller) =>
        controller.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .FirstOrDefault(c => c.HasSubtype(CardSubtype.Snail));

    /// <summary>
    /// CR 111.4 — create a 1/1 black Snail creature token under
    /// <paramref name="controller"/>. Black colour is stamped via
    /// <see cref="TokenFactory.TokenSpec.Colors"/>; the token enters the
    /// battlefield, firing <see cref="CardMovedEvent"/> when a
    /// <paramref name="zones"/> service is supplied.
    /// </summary>
    public static Creature CreateSnailToken(Player controller, ZoneService? zones)
    {
        var spec = new TokenFactory.TokenSpec(
            Name: "Snail",
            Power: SnailTokenPower,
            Toughness: SnailTokenToughness,
            Subtypes: new[] { CardSubtype.Snail },
            Colors: new[] { ManaColor.Black });

        return TokenFactory.CreateOnBattlefield(spec, controller, zones);
    }
}
