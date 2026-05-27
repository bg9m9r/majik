using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Wall of Omens (Rise of the Eldrazi / various reprints,
/// {1}{W}).
///
/// Creature — Wall 0/4. Oracle text:
///   "Defender.
///    When this creature enters, draw a card."
///
/// ## Implemented (v1)
/// - Card identity: 0/4 Creature — Wall, mana cost {1}{W}.
/// - <b>Defender keyword</b> (CR 702.3) — wired as a
///   <see cref="KeywordAbility"/> marker so
///   <see cref="Majik.Core.Combat.CombatAbilities.HasDefender"/> surfaces it
///   (combat block legality treats the card as a blocker only).
/// - <b>ETB triggered ability (CR 603.6a)</b>: when Wall of Omens enters the
///   battlefield, its controller draws a card (top of library → hand).
///   If the library is empty,
///   <see cref="Player.MarkTriedToDrawFromEmptyLibrary"/> is called so SBAs
///   can resolve loss on the next opportunity (CR 120.3).
/// - Single-arg dispatcher path attaches the ETB trigger for shape tests;
///   the (owner, eventBus, triggers) overload registers the trigger with a
///   live <see cref="TriggerManager"/> so a <see cref="CardMovedEvent"/>
///   to the battlefield places it on the stack automatically (CR 603.3).
///
/// ## Deferred (v1 gaps)
/// - <b>Player-driven activation prompt</b>: the ETB draw resolves
///   immediately via effect execution; full stack/priority resolution is
///   handled by the engine's TriggerManager integration.
/// </summary>
[CardName("Wall of Omens")]
public static class WallOfOmensFactory
{
    public const string CardName = "Wall of Omens";
    public const string PrintedManaCost = "{1}{W}";
    public const int Power = 0;
    public const int Toughness = 4;

    /// <summary>
    /// Construct Wall of Omens with no live bus / trigger-manager wiring.
    /// The ETB trigger is attached to the card but not registered with a
    /// <see cref="TriggerManager"/>. Suitable for shape / dispatcher tests.
    /// </summary>
    public static Creature Create(Player owner) =>
        Create(owner, eventBus: null, triggers: null);

    /// <summary>
    /// Construct Wall of Omens with optional event bus and trigger manager.
    /// When <paramref name="triggers"/> is supplied, the ETB trigger is
    /// registered so a <see cref="CardMovedEvent"/> to the battlefield
    /// automatically places it on the stack (CR 603.3).
    /// </summary>
    /// <param name="owner">Card owner / initial controller.</param>
    /// <param name="eventBus">Event bus — unused in v1 but accepted for
    /// API symmetry with other ETB-draw factories.</param>
    /// <param name="triggers">Trigger manager to register the ETB ability
    /// with. May be null for shape / unit tests.</param>
    public static Creature Create(Player owner, IEventBus? eventBus, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Wall });

        card.SetOwner(owner);
        card.SetController(owner);

        // CR 702.3 — Defender keyword marker. Wired so
        // CombatAbilities.HasDefender surfaces it for block-legality
        // (BlockLegality.cs reads this via the KeywordAbility-marker
        // fallback path).
        card.AddAbility(new KeywordAbility("Defender", card, owner));

        // ----------------------------------------------------------------
        // ETB triggered ability — CR 603.6a.
        //   "When this creature enters, draw a card."
        // Unconditionally draws 1 card for the controller on entering the
        // battlefield. Simpler than Silvergill Adept — no reveal-or-pay
        // additional cost, no intervening-if.
        // ----------------------------------------------------------------
        var etbEffect = new Effect(
            "Wall of Omens — controller draws a card on ETB",
            () =>
            {
                var top = owner.Zones.Library.GetCards().FirstOrDefault();
                if (top == null)
                {
                    // CR 120.3 — empty library; SBA resolves loss on next pass.
                    owner.MarkTriedToDrawFromEmptyLibrary();
                    return;
                }

                // Move Library → Hand (CR 121).
                owner.Zones.Library.RemoveCard(top);
                owner.Zones.Hand.AddCard(top);
                top.SetZone(ZoneType.Hand);
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
