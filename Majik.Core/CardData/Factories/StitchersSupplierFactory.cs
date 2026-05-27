using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stitcher's Supplier (Core Set 2019, {B}).
///
/// Creature — Zombie 1/1. Oracle text:
///   "When this creature enters or dies, mill three cards.
///    (Put the top three cards of your library into your graveyard.)"
///
/// ## Implemented (v1)
/// - 1/1 Creature — Zombie, mana cost {B}.
/// - <b>ETB trigger (CR 603.6a)</b> — fires on
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>; mills 3 from the
///   controller's library via <see cref="MillAction.Apply"/>.
/// - <b>Dies trigger (CR 603.6c, Rule 700.4)</b> — fires on
///   <see cref="Triggers.OnDies"/>; mills 3 from the controller's
///   library. Active zones include <see cref="ZoneType.Graveyard"/> so
///   the trigger remains observable after ZoneService stamps
///   <c>card.Zone = Graveyard</c> before publishing the
///   <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/> —
///   same pattern Young Wolf / Undying uses.
///
/// ## Mill semantics (CR 701.13)
/// Both triggers call <see cref="MillAction.Apply"/> with N=3. If the
/// library has fewer than 3 cards, all remaining cards are milled and
/// the trigger does NOT directly cause the player to lose — the loss
/// only happens later via the empty-library draw-step SBA (CR 704.5b).
/// "Controller" is read off the source card at resolution time
/// (Stitcher's Supplier's controller for ETB; the dying creature's
/// last-known controller for the dies trigger — read from
/// <see cref="Permanent.Controller"/> which survives the zone move).
///
/// ## Wiring
/// - <see cref="Create(Player)"/> attaches both triggers to the card
///   shape but does NOT register them with a
///   <see cref="TriggerManager"/>. Suitable for dispatcher / shape
///   tests — mirrors Young Wolf's two-arg pattern.
/// - <see cref="Create(Player, TriggerManager)"/> additionally
///   registers both triggers with the live <see cref="TriggerManager"/>
///   so an ETB / dies <see cref="Majik.Core.Domain.DomainEvents.CardMovedEvent"/>
///   places them on the stack automatically.
///
/// ## Comparison
/// - ETB-only mill cards (e.g. Wrenn and Realmbreaker's +1 mill self) go
///   straight through <see cref="MillAction.Apply"/> in the resolve
///   effect.
/// - Dies-only triggers (Young Wolf / Undying) wire a single
///   <see cref="Triggers.OnDies"/> trigger. Stitcher's Supplier is the
///   common case of BOTH paths firing the same effect — modelled here
///   as two distinct triggered abilities sharing an effect builder so
///   the trigger surfaces stay independent (the engine has no concept
///   of an OR'd condition object).
/// </summary>
[CardName("Stitcher's Supplier")]
public static class StitchersSupplierFactory
{
    public const string CardName = "Stitcher's Supplier";
    public const string PrintedManaCost = "{B}";
    public const int Power = 1;
    public const int Toughness = 1;

    /// <summary>Number of cards milled by each trigger
    /// (printed value).</summary>
    public const int MillCount = 3;

    /// <summary>
    /// Construct Stitcher's Supplier with both triggers attached to the
    /// card shape but NOT registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher
    /// tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Stitcher's Supplier with optional
    /// <see cref="TriggerManager"/> wiring. When
    /// <paramref name="triggers"/> is supplied, BOTH the ETB and dies
    /// triggers are registered so they fire automatically.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Zombie });

        card.SetOwner(owner);
        card.SetController(owner);

        // The single resolve effect both triggers share — mill 3 from
        // the controller's library (CR 701.13). Reads the controller at
        // resolution time so a control-change between trigger placement
        // and resolution mills the *current* controller (CR 608.2 —
        // resolve under current game state).
        IEffect BuildMillEffect(string label) => new Effect(
            label,
            () =>
            {
                var controller = card.Controller ?? owner;
                MillAction.Apply(controller, MillCount);
            });

        // ETB trigger (CR 603.6a) — "When this creature enters, mill
        // three cards." Active on Battlefield only.
        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { BuildMillEffect($"{CardName} ETB: mill {MillCount}") },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // Dies trigger (CR 603.6c, Rule 700.4) — "When this creature
        // dies, mill three cards." Active on Battlefield + Graveyard
        // because ZoneService stamps Zone = Graveyard BEFORE publishing
        // the CardMovedEvent (mirrors Young Wolf / Undying).
        var diesTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnDies(card),
            effects: new IEffect[] { BuildMillEffect($"{CardName} dies: mill {MillCount}") },
            activeZones: new[] { ZoneType.Battlefield, ZoneType.Graveyard });

        card.AddAbility(diesTrigger);
        triggers?.RegisterTriggeredAbility(diesTrigger);

        return card;
    }
}
