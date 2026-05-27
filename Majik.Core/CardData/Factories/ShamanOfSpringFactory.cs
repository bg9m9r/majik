using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Shaman of Spring (Magic 2015 / various, {3}{G}).
///
/// Creature — Elf Shaman 2/2. Oracle text:
///   "When this creature enters, draw a card."
///
/// ## Implemented (v1)
/// - 2/2 Creature — Elf Shaman, mana cost {3}{G}. Owner / controller wired.
/// - <b>ETB triggered ability (CR 603.6a)</b> via
///   <see cref="Triggers.OnEnterBattlefieldSelf"/>: on resolve the
///   controller draws 1 card via <see cref="Fx.DrawCards"/>. Routing
///   through <see cref="Fx.DrawCards"/> means replacement effects
///   (Dredge, Alms Collector, etc.) intercept the draw per-draw, and an
///   empty library stamps the CR 704.5b loss flag without crashing.
///
/// ## Wiring overloads
///
/// - <see cref="Create(Player)"/> — card shape only. The ETB trigger is
///   attached for shape observability; NOT registered with any trigger
///   manager. Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. ETB trigger registered with <paramref name="triggers"/> when
///   supplied so live bus events queue it. <paramref name="eventBus"/> is
///   accepted for future lifecycle hooks (not consumed in v1).
/// </summary>
[CardName("Shaman of Spring")]
public static class ShamanOfSpringFactory
{
    public const string CardName = "Shaman of Spring";
    public const string PrintedManaCost = "{3}{G}";
    public const int Power = 2;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Shaman of Spring with no live wiring. The ETB trigger is
    /// attached to the card so structural / dispatcher tests observe its
    /// shape; the trigger is NOT registered (no trigger manager supplied).
    /// Suitable for dispatcher / shape tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Shaman of Spring with optional runtime services.
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Reserved for future lifecycle hooks; not
    /// consumed in v1.</param>
    /// <param name="triggers">TriggerManager for the ETB draw trigger.
    /// May be null — the trigger is still attached to the card shape.</param>
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
            supertypes: Array.Empty<CardSupertype>(),
            subtypes: new[] { CardSubtype.Elf, CardSubtype.Shaman });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB — "When this creature enters, draw a card." CR 603.6a.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            $"{CardName}: ETB — draw a card",
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
