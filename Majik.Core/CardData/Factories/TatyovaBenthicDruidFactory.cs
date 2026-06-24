using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Tatyova, Benthic Druid (Dominaria, {3}{G}{U}).
///
/// Legendary Creature — Merfolk Druid 3/3. Oracle text:
///   "Landfall — Whenever a land you control enters, you gain 1 life and
///    draw a card."
///
/// Hand-rolled exactly like <see cref="LotusCobraFactory"/> /
/// <see cref="RuinCrabFactory"/> (the landfall family): the unique
/// behaviour is the shared landfall trigger predicate
/// (<see cref="Triggers.OnLandEntersUnderControl"/>) paired with a
/// resolve that gains 1 life (CR 119.3) and draws a card (CR 120). Both
/// resolve verbs are existing engine primitives
/// (<see cref="Fx.GainLife"/> / <see cref="Fx.DrawCards"/>); no new
/// effect / keyword / binder infra is introduced.
///
/// ## Implemented (v1)
/// - 3/3 Legendary Creature — Merfolk Druid, mana cost {3}{G}{U},
///   owner / controller stamped.
/// - <b>Landfall triggered ability</b> (CR 603.1 / 603.6a / CR 614) —
///   fires on the shared <see cref="Triggers.OnLandEntersUnderControl"/>
///   condition: a <see cref="Majik.Core.Events.CardMovedEvent"/> with
///   destination = Battlefield, the moved card has the Land card type, and
///   its controller matches Tatyova's controller. Identical predicate to
///   Lotus Cobra / Ruin Crab / Hedron Crab and the rest of the landfall
///   family.
/// - <b>Resolve — gain 1 life and draw a card</b> (CR 119.3 / CR 120):
///   the controller (resolved live, CR 109.5) gains 1 life via
///   <see cref="Fx.GainLife"/> then draws one card via
///   <see cref="Fx.DrawCards"/> (empty-library loss marked inside
///   <c>DrawCards</c> per CR 120.3 / 704.5b). Untargeted (CR 115.1a) — no
///   <c>TargetRequest</c>.
///
/// ## Deferred (v1 gaps)
/// - <b>Trigger registration</b>: the <see cref="Create(Player)"/> path
///   attaches the trigger to the card for shape inspection but does not
///   register it with a bus; pass a <see cref="TriggerManager"/> for live
///   firing (same convention as the rest of the landfall family).
/// </summary>
[CardName("Tatyova, Benthic Druid")]
public static class TatyovaBenthicDruidFactory
{
    public const string CardName = "Tatyova, Benthic Druid";
    public const string PrintedManaCost = "{3}{G}{U}";

    public const int Power = 3;
    public const int Toughness = 3;

    public const int LifeGain = 1;
    public const int DrawCount = 1;

    /// <summary>
    /// Construct Tatyova with no live <see cref="TriggerManager"/> wiring.
    /// The landfall trigger is attached for shape inspection but not
    /// registered with a bus. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Tatyova. When <paramref name="triggers"/> is supplied the
    /// landfall trigger is registered so a
    /// <see cref="Majik.Core.Events.CardMovedEvent"/> for a land entering
    /// under the controller's control automatically queues the ability.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="triggers">When supplied, the landfall trigger is
    /// registered with the bus so a land entering under the controller's
    /// control surfaces the ability as pending.</param>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            supertypes: new[] { CardSupertype.Legendary },
            subtypes: new[] { CardSubtype.Merfolk, CardSubtype.Druid });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // Landfall — CR 603.1 / 603.6a / CR 614. "Whenever a land you
        // control enters, you gain 1 life and draw a card." Untargeted
        // (CR 115.1a). At resolution the controller (resolved live,
        // CR 109.5) gains 1 life (CR 119.3) then draws one card (CR 120).
        // Same shared landfall predicate as Lotus Cobra / Ruin Crab.
        // ----------------------------------------------------------------
        var landfallEffect = new Effect(
            $"{CardName}: landfall — gain {LifeGain} life and draw {DrawCount} card",
            () =>
            {
                var controller = card.Controller ?? owner;
                Fx.GainLife(controller, LifeGain);
                Fx.DrawCards(controller, DrawCount);
            });

        var landfallTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: Triggers.OnLandEntersUnderControl(owner),
            effects: new IEffect[] { landfallEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(landfallTrigger);
        triggers?.RegisterTriggeredAbility(landfallTrigger);

        return card;
    }
}
