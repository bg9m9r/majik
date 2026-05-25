using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Quantum Riddler (Edge of Eternities, {3}{U}{U}).
///
/// Creature — Sphinx 4/6. Oracle text (verified Scryfall 2026-05-24):
///   "Flying
///    When this creature enters, draw a card.
///    As long as you have one or fewer cards in hand, if you would draw
///    one or more cards, you draw that many cards plus one instead.
///    Warp {1}{U}"
///
/// ## Implemented (v1)
/// - 4/6 Sphinx Creature with mana cost {3}{U}{U}.
/// - <b>Flying (CR 702.9)</b> as a <see cref="KeywordAbility"/> marker
///   (same posture as Abhorrent Oculus / Atraxa / Sprite Dragon — combat
///   pipeline consumes the keyword marker).
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature
///   enters, draw a card." Resolution calls <see cref="Fx.DrawCards"/>(1)
///   on the controller, which moves the top of the library to hand and
///   stamps <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> on
///   empty-library (CR 120.3 / 704.5b). Same shape as Silvergill Adept's
///   ETB draw.
/// - <b>Warp keyword marker</b> (CR 702.??? — Edge of Eternities) as a
///   <see cref="KeywordAbility"/>. Mechanic deferred — same posture as
///   <see cref="PinnacleEmissaryFactory"/>.
///
/// ## Deferred (v1 gaps)
/// - <b>Conditional draw-replacement clause</b>: "As long as you have one
///   or fewer cards in hand, if you would draw one or more cards, you
///   draw that many cards plus one instead." The engine has no
///   CardDrawIntent on the <see cref="Majik.Core.Effects.ReplacementBus"/>
///   in v1 — every replacement effect that intercepts a draw (Spirit of
///   the Labyrinth, Alms Collector, Necrodominance's "skip additional
///   draws", Sylvan Library's draw-three) ships as a structural
///   <see cref="StaticAbility"/> marker until that intent shape lands.
///   Same v1 gap as Necrodominance — see
///   <see cref="NecrodominanceFactory"/>'s "skip additional draws"
///   marker. When CardDrawIntent lands the marker swaps for an
///   <see cref="IReplacementEffect"/> that bumps the requested draw count
///   by +1 while <c>controller.Zones.Hand.Count &lt;= 1</c> AND Quantum
///   Riddler is on the battlefield (CR 614.12).
/// - <b>Warp alt-cost (CR 702.??? — new Edge of Eternities keyword)</b>:
///   deferred infra. See <see cref="PinnacleEmissaryFactory"/>'s xmldoc
///   for the full description of the missing primitive (Warp {cost} +
///   exile-at-next-end-step + cast-from-exile-later, parallels
///   Suspend → Plot → Warp evolution). v1 ships Quantum Riddler at its
///   printed {3}{U}{U} cast cost with a "Warp" keyword marker for
///   card-text inspection.
/// </summary>
[CardName("Quantum Riddler")]
public static class QuantumRiddlerFactory
{
    public const string CardName = "Quantum Riddler";
    public const string PrintedManaCost = "{3}{U}{U}";
    public const int Power = 4;
    public const int Toughness = 6;

    /// <summary>
    /// Construct Quantum Riddler with no live bus / trigger-manager
    /// wiring. The ETB trigger is attached to the card shape so
    /// dispatcher / structural tests can observe it; live firing
    /// requires the (owner, eventBus, triggers) overload. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Quantum Riddler. When <paramref name="triggers"/> is
    /// supplied the ETB trigger is registered so a
    /// <see cref="CardMovedEvent"/> to the battlefield automatically
    /// places it on the stack (CR 603.3); otherwise the trigger is
    /// attached structurally but not registered for firing.
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
            subtypes: new[] { CardSubtype.Sphinx });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // CR 702.9 — Flying. KeywordAbility marker consumed by the combat
        // block-validation pipeline (mirrors Abhorrent Oculus / Sprite
        // Dragon / Atraxa).
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Flying", card, owner));

        // ----------------------------------------------------------------
        // Warp keyword marker (CR 702.??? — Edge of Eternities). The
        // mechanic (alt-cost + exile-at-end-step + cast-from-exile-later)
        // is deferred; the marker surfaces the keyword for card-text
        // inspection — same posture as PinnacleEmissaryFactory.
        // ----------------------------------------------------------------
        card.AddAbility(new KeywordAbility("Warp", card, owner));

        // ----------------------------------------------------------------
        // Structural-only marker — "As long as you have one or fewer
        // cards in hand, if you would draw one or more cards, you draw
        // that many cards plus one instead."
        //
        // The engine has no CardDrawIntent on the ReplacementBus in v1,
        // so the conditional additional-draw clause ships as a
        // declarative StaticAbility marker. When CardDrawIntent lands,
        // swap this for a real IReplacementEffect that bumps the
        // requested draw count by +1 while the controller's hand size
        // is <= 1 AND Quantum Riddler is on the battlefield (CR 614.12).
        // Same v1 gap as Necrodominance's "skip additional draws" clause.
        // ----------------------------------------------------------------
        card.AddAbility(new StaticAbility(
            source: card,
            controller: owner,
            description:
                "As long as you have one or fewer cards in hand, if you would draw "
                + "one or more cards, you draw that many cards plus one instead.",
            isActiveCheck: () => card.Zone == ZoneType.Battlefield
                                  && (card.Controller?.Zones.Hand.GetCards().Count() ?? int.MaxValue) <= 1,
            applyEffect: null));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, draw a card."
        //
        // Resolution: controller draws one card via Fx.DrawCards (top of
        // library → hand; empty-library stamps the SBA loss marker per
        // CR 120.3 / 704.5b). Same shape as Silvergill Adept's ETB.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: controller draws a card on ETB",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.DrawCards(controller, 1);
            });

        var etbTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnEnterBattlefieldSelf(card),
            effects: new IEffect[] { etbEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbTrigger);
        triggers?.RegisterTriggeredAbility(etbTrigger);

        return card;
    }
}
