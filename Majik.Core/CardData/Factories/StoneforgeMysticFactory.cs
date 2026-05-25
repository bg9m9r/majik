using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Stoneforge Mystic (Worldwake, {1}{W}).
///
/// Creature — Kor Artificer 1/2. Oracle text:
///   "When Stoneforge Mystic enters, you may search your library for an
///    Equipment card, reveal it, put it into your hand, then shuffle.
///    {1}{W}, {T}: You may put an Equipment card from your hand onto the
///    battlefield. Then attach it to a creature you control."
///
/// ## Implemented (v1)
/// - 1/2 Kor Artificer with mana cost {1}{W}.
/// - <b>ETB tutor (CR 701.19a)</b>: When Stoneforge Mystic enters, the
///   controller's library is searched deterministically for the first
///   Equipment card; if found, it is moved Library → Hand. Per CR 701.19a
///   the search is a "may" and the v1 deterministic picker simply takes
///   the first eligible card. The single-arg factory attaches the trigger
///   to the card but does NOT register it with a TriggerManager; tests
///   exercise the ETB effect by either firing the trigger manually or
///   driving the card through ZoneService (which publishes the
///   <see cref="CardMovedEvent"/> that the trigger consumes).
/// - <b>Activated alt-zone-cast ability (CR 113.6c, 117.1a)</b>:
///   <c>{1}{W}, {T}: You may put an Equipment card from your hand onto
///   the battlefield. Then attach it to a creature you control.</c> The
///   activation requires a controller-side decision at resolution time
///   (CR 117 — choices are made on resolution, not on activation). The
///   v1 picker is deterministic: it takes the first Equipment in hand and
///   attaches to the first creature on the controller's battlefield.
///   Movement is funnelled through <see cref="ZoneService.MoveCard"/>
///   when a service is supplied, so ETB-replacement effects on the
///   Equipment fire (CR 614 / 603.6a). When no ZoneService is supplied
///   the move falls back to raw zone manipulation suitable for shape
///   tests.
///
/// ## Deferred (v1 gaps)
/// - <b>Hand-search agent prompt</b>: the activated ability hard-codes
///   "first Equipment in hand". A full implementation would prompt the
///   controller's agent (CR 701.19a style) for which Equipment to put
///   onto the battlefield, including the "you may" opt-out clause.
/// - <b>Attach target prompt</b>: "attach it to a creature you control"
///   should prompt the controller for any of their creatures (CR 701.3a).
///   v1 auto-picks the first creature on the controller's battlefield.
/// - <b>Reveal event</b>: the ETB tutor moves the card to hand without
///   emitting a CardRevealedEvent. Wire a reveal when CardRevealedEvent
///   plumbing is exercised by an in-engine prompt path.
/// </summary>
[CardName("Stoneforge Mystic")]
public static class StoneforgeMysticFactory
{
    /// <summary>
    /// Construct Stoneforge Mystic with no live ZoneService / TriggerManager
    /// wiring (the shape/dispatcher path). The ETB trigger is attached but
    /// not registered. The activated ability falls back to raw zone moves —
    /// suitable for unit / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, zoneService: null, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Stoneforge Mystic with optional runtime services.
    /// When <paramref name="zoneService"/> is supplied, the activated
    /// ability uses <see cref="ZoneService.MoveCard"/> for the
    /// hand → battlefield move (so ETB replacements + triggers on the
    /// Equipment fire). When <paramref name="triggers"/> is supplied, the
    /// ETB trigger is registered so a <see cref="CardMovedEvent"/> to
    /// the battlefield places it on the stack automatically.
    /// </summary>
    public static Creature Create(
        Player owner,
        ZoneService? zoneService,
        IEventBus? eventBus,
        TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: "Stoneforge Mystic",
            manaCost: "{1}{W}",
            power: 1,
            toughness: 2,
            subtypes: new[] { CardSubtype.Kor, CardSubtype.Artificer });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.1.
        //   "When Stoneforge Mystic enters, you may search your library for
        //    an Equipment card, reveal it, put it into your hand, then
        //    shuffle."
        // v1: deterministic — take the first Equipment card in the library
        // (Artifact whose subtypes include Equipment). CR 701.20a shuffle is
        // wired via LibraryShuffle (publishes a LibraryShuffledEvent when an
        // EventBus is registered). Reveal-event emission is the only
        // outstanding gap (see class xmldoc).
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Stoneforge Mystic: tutor an Equipment to hand",
            () =>
            {
                var pick = owner.Zones.Library.GetCards()
                    .FirstOrDefault(c => c.HasSubtype(CardSubtype.Equipment));
                if (pick == null) return; // CR 701.19a — declined / no candidate is legal.

                owner.Zones.Library.RemoveCard(pick);
                owner.Zones.Hand.AddCard(pick);
                pick.SetZone(ZoneType.Hand);
                // CR 701.20a — shuffle after the search resolves.
                Majik.Core.Zones.LibraryShuffle.ShuffleLibrary(owner, "stoneforge-mystic");
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        // ----------------------------------------------------------------
        // Activated ability — {1}{W}, {T}: put an Equipment from hand onto
        // battlefield, then attach to a creature you control.
        //
        // CR 113.6c / 117.1a — putting a permanent directly onto the
        // battlefield is NOT casting, but the effect surface is analogous.
        // CR 117 — controller-side choices ("which Equipment", "which
        // creature") are made at resolution time, not on activation.
        // CR 603.6a — the hand → battlefield move funnels through
        // ZoneService so ETB triggers + replacements on the Equipment
        // fire (PR #165).
        // v1 picker is deterministic — see class xmldoc.
        // ----------------------------------------------------------------
        var activatedEffect = new Effect(
            "Stoneforge Mystic: put Equipment from hand to battlefield, then attach",
            () =>
            {
                // Choose the Equipment at resolution time (CR 117.1a).
                var equipment = owner.Zones.Hand.GetCards()
                    .OfType<Permanent>()
                    .FirstOrDefault(p => p.HasSubtype(CardSubtype.Equipment));

                // "You may …" + "if you can't, …" — declining or having no
                // Equipment in hand resolves as a no-op (CR 605.1 / 117.x —
                // a may-effect with no valid execution simply does nothing).
                if (equipment == null) return;

                // Move hand → battlefield. Prefer ZoneService so ETB
                // triggers / replacements on the Equipment fire (CR 603.6a,
                // CR 614). Fall back to raw zone manipulation when no
                // service is wired (test / shape path).
                if (zoneService != null)
                {
                    zoneService.MoveCard(equipment, ZoneType.Hand, ZoneType.Battlefield, owner);
                }
                else
                {
                    owner.Zones.Hand.RemoveCard(equipment);
                    owner.Zones.Battlefield.AddCard(equipment);
                    equipment.SetZone(ZoneType.Battlefield);
                    equipment.SetController(owner);
                }

                // Then attach to a creature the controller controls
                // (CR 701.3a). v1 picks the first creature deterministically.
                var bearer = owner.Zones.Battlefield.GetCards()
                    .OfType<Creature>()
                    .FirstOrDefault(c => ReferenceEquals(c.Controller, owner));
                if (bearer == null) return; // No creature → Equipment sits unattached.

                equipment.AttachTo(bearer);
            });

        var activatedAbility = new ActivatedAbility(
            source: card,
            controller: owner,
            costs: new ICost[]
            {
                new ManaCostCost("{1}{W}"),
                AdditionalCost.Tap(card),
            },
            effects: new IEffect[] { activatedEffect });

        card.AddAbility(activatedAbility);

        return card;
    }
}
