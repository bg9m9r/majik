using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Primitives;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Generous Stray (Ikoria: Lair of Behemoths, {2}{G}).
///
/// Creature — Cat 1/2. Oracle text:
///   "When this creature enters, draw a card."
///
/// ## Implemented (v1)
/// - 1/2 Cat Creature with mana cost {2}{G} (mana value 3, CR 202.3).
/// - <b>ETB triggered ability (CR 603.6a)</b>: "When this creature
///   enters, draw a card." Resolution calls <see cref="Fx.DrawCards"/>(1)
///   on the controller, which moves the top of the library to hand and
///   stamps <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> on
///   empty-library (CR 120.3 / 704.5b). Same shape as Quantum Riddler /
///   Silvergill Adept ETB draw.
/// - No evasion keywords — Generous Stray has no Flying or other combat
///   keywords.
///
/// ## Wiring overloads
/// - <see cref="Create(Player)"/> — card shape only. The ETB trigger is
///   attached for structural inspection; it is NOT registered (no trigger
///   manager supplied). Suitable for dispatcher / shape tests.
/// - <see cref="Create(Player, IEventBus?, TriggerManager?)"/> — fully
///   wired. The ETB trigger is registered when <paramref name="triggers"/>
///   is supplied so a <see cref="CardMovedEvent"/> to the battlefield
///   automatically places it on the stack (CR 603.3).
/// </summary>
[CardName("Generous Stray")]
public static class GenerousStrayFactory
{
    public const string CardName = "Generous Stray";
    public const string PrintedManaCost = "{2}{G}";
    public const int Power = 1;
    public const int Toughness = 2;

    /// <summary>
    /// Construct Generous Stray with no live bus / trigger-manager
    /// wiring. The ETB trigger is attached to the card shape so
    /// dispatcher / structural tests can observe it; live firing
    /// requires the (owner, eventBus, triggers) overload. Suitable for
    /// shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Generous Stray. When <paramref name="triggers"/> is
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
            subtypes: new[] { CardSubtype.Cat });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, draw a card."
        //
        // Resolution: controller draws one card via Fx.DrawCards (top of
        // library → hand; empty-library stamps the SBA loss marker per
        // CR 120.3 / 704.5b). Same shape as Quantum Riddler / Silvergill
        // Adept ETB draw. No Flying or other keywords — Generous Stray
        // has no evasion.
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
