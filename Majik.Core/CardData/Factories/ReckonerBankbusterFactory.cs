using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Services;
using Majik.Core.Tokens;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Reckoner Bankbuster (The Brothers' War, {2}).
///
/// Artifact — Vehicle. Oracle text:
///   "Reckoner Bankbuster enters with three charge counters on it."
///   "Whenever Reckoner Bankbuster attacks, put a charge counter on it."
///   "{T}, Remove a charge counter from Reckoner Bankbuster: Draw a card.
///    Then if there are no charge counters on Reckoner Bankbuster, create
///    a Powerstone token."
///   "Crew 2."
///
/// ## Implemented (v1)
///
/// - Shell: <see cref="Creature"/> with <see cref="CardType.Artifact"/>
///   additively stamped (CR 301.1 / 302.1 — Artifact Vehicle multi-type
///   pattern; mirrors <see cref="EsikasChariotFactory"/> /
///   <see cref="SmugglersCopterFactory"/>). Base P/T is the printed 0/4
///   (Bankbuster's crewed body) so <see cref="CardData.Vehicles.CrewAction"/>
///   ships those values through <see cref="Majik.Core.Effects.VehicleCrewEffect"/>
///   when crewed.
/// - <b>ETB — three charge counters</b> (CR 121 / CR 122 — counters; the
///   "enters with" phrasing is a CR 614.1d-style ETB replacement in
///   official rules, but Bankbuster never interacts with the
///   <see cref="Majik.Core.Effects.EntersWithCountersReplacement"/>
///   pipeline because that path only carries the +1/+1 channel. We
///   model the printed effect inline via an ETB <see cref="TriggeredAbility"/>
///   that places three <see cref="CounterType.Charge"/> counters at
///   battlefield entry — same shape <see cref="ChaliceOfTheVoidFactory"/>
///   uses for its X-charge ETB).
/// - <b>Attack trigger</b> (CR 508.1f / CR 603.1): "Whenever Reckoner
///   Bankbuster attacks, put a charge counter on it." Wired via
///   <see cref="Triggers.OnAttackSelf"/> over
///   <see cref="CreatureAttacksEvent"/>; resolution adds one charge
///   counter.
/// - <b>Activated ability — {T}, Remove a charge counter: Draw a card.
///   Then if there are no charge counters, create a Powerstone token.</b>
///   (CR 602.1.) Costs are <see cref="AdditionalCost.Tap"/> +
///   <see cref="RemoveChargeCounterCost"/>. Effect draws one card via
///   <see cref="Player.DrawCard"/>; then a post-resolution check inspects
///   the source's <see cref="CounterCollection"/> — if zero
///   <see cref="CounterType.Charge"/> remain, a Powerstone artifact token
///   is created via <see cref="TokenFactory.CreatePowerstone"/> under the
///   activator's control.
/// - <b>Crew 2</b> (CR 702.122): surfaced as structural data on
///   <see cref="CrewCost"/>; callers route through
///   <see cref="CardData.Vehicles.CrewAction.Crew"/> exactly as every
///   other Vehicle MVP does.
///
/// ## Deferred (v1 gaps)
///
/// - <b>"Enters with N counters" through ReplacementBus</b>:
///   <see cref="Majik.Core.Effects.EntersWithCountersReplacement"/>
///   only covers +1/+1 today. Modelling Bankbuster's three-charge ETB
///   as a TriggeredAbility is sound (CR 614 — ETB replacements and ETB
///   triggers both fire at battlefield arrival; the difference is rules-
///   internal, not observable to a card that doesn't interact with
///   Doubling Season for non-+1/+1 counters). Doubling Season DOES
///   double charge counters by CR 614 — that interaction is a separate
///   slice; Bankbuster ships without it for now.
/// - <b>Powerstone spend-restriction enforcement</b>: the
///   <see cref="Mana.SpendRestriction"/> rider is stamped on the
///   generated mana (see <see cref="TokenFactory.CreatePowerstone"/>)
///   but the payment-time gate is shared infrastructure pending the
///   <see cref="Majik.Core.ValueObjects.ManaPool"/> per-slot provenance
///   work. Until then, Powerstone mana effectively pays anything — same
///   v1 gap shared with Cavern of Souls / Eldrazi Temple.
/// - <b>Crew as an activated ability</b>: kept as structural data; tests
///   call <see cref="CardData.Vehicles.CrewAction.Crew"/> directly, same
///   shape as the rest of the Vehicle MVP.
/// </summary>
[CardName("Reckoner Bankbuster")]
public static class ReckonerBankbusterFactory
{
    public const string CardName = "Reckoner Bankbuster";
    public const string PrintedManaCost = "{2}";
    public const int CrewCost = 2;
    public const int VehiclePower = 0;
    public const int VehicleToughness = 4;
    public const int StartingChargeCounters = 3;

    /// <summary>
    /// Construct Reckoner Bankbuster with no live wiring. ETB / attack
    /// triggers and the activated "draw a card" ability are attached to
    /// the card shape; no <see cref="TriggerManager"/> registration.
    /// Suitable for dispatcher / structural tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Reckoner Bankbuster with optional runtime services. When
    /// <paramref name="triggers"/> is supplied the ETB + attack triggers
    /// are registered with the bus so the corresponding events queue the
    /// abilities automatically. When <paramref name="zoneService"/> is
    /// supplied the Powerstone token created by the activated ability's
    /// "no charge counters left" branch routes through ZoneService so
    /// <see cref="CardMovedEvent"/> fires (downstream ETB listeners see
    /// the token's arrival).
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: VehiclePower,
            toughness: VehicleToughness,
            subtypes: new[] { CardSubtype.Vehicle });

        // CR 301.1 / 302.1 — Bankbuster is an Artifact (Vehicle). Stamp
        // the Artifact card type on top of the Creature shell so
        // HasType-based lookups see it (mirrors Esika's Chariot,
        // Smuggler's Copter).
        card.AddCardType(CardType.Artifact);

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB — "Reckoner Bankbuster enters with three charge counters on
        // it." (CR 614 / CR 122.)
        // Modelled as an ETB TriggeredAbility because
        // EntersWithCountersReplacement only covers +1/+1 today.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: enter with {StartingChargeCounters} charge counters",
            () => card.Counters.Add(CounterType.Charge, StartingChargeCounters));

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Attack trigger — CR 508.1f / CR 603.1.
        //   "Whenever Reckoner Bankbuster attacks, put a charge counter
        //    on it."
        // ----------------------------------------------------------------
        var attackEffect = new Effect(
            $"{CardName}: put a charge counter on itself",
            () =>
            {
                if (card.Zone != ZoneType.Battlefield) return;
                card.Counters.Add(CounterType.Charge, 1);
            });

        var attackTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnAttackSelf(card),
            effects: new IEffect[] { attackEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(attackTrigger);
        triggers?.RegisterTriggeredAbility(attackTrigger);

        // ----------------------------------------------------------------
        // Activated ability — CR 602.1.
        //   "{T}, Remove a charge counter from Reckoner Bankbuster: Draw
        //    a card. Then if there are no charge counters on Reckoner
        //    Bankbuster, create a Powerstone token."
        // Cost: tap + remove a charge counter.
        // Effect: controller draws 1; if the source has zero charge
        // counters after the draw (CR 605 — "then if" tail clauses check
        // game state at resolution), create a Powerstone token under the
        // activating controller.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            $"{CardName}: draw a card; if no charge counters remain, create a Powerstone",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, 1);

                // "Then if there are no charge counters on ~, create a
                // Powerstone token." (CR 605 conditional tail-clause —
                // check game state at resolution, after the cost has
                // been paid AND the draw has resolved.)
                if (card.Counters.Count(CounterType.Charge) == 0)
                {
                    TokenFactory.CreatePowerstone(controller, zoneService);
                }
            });

        var activated = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                AdditionalCost.Tap(card),
                new RemoveChargeCounterCost(card),
            },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activated);

        return card;
    }
}
