using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.CardData.Factories;

/// <summary>
/// Named-card factory for Soul's Attendant (Magic 2011, {W}).
///
/// Creature — Human Cleric 1/1. Functional reprint of Soul Warden with the
/// same Scryfall oracle:
///   "Whenever another creature enters, you gain 1 life."
///
/// Implementation mirrors <see cref="SoulWardenFactory"/> exactly — kept as a
/// separate factory (rather than aliased) so the
/// <c>[CardName]</c> dispatcher table holds the printed-name identity
/// independently and the Modern Soul Sisters archetype can field both
/// (max 8 effective Soul Wardens per CR 113 deck rules).
/// </summary>
[CardName("Soul's Attendant")]
public static class SoulsAttendantFactory
{
    public const string CardName = "Soul's Attendant";
    public const string PrintedManaCost = "{W}";
    public const int Power = 1;
    public const int Toughness = 1;
    public const int LifeGainAmount = 1;

    /// <summary>
    /// Construct Soul's Attendant with no live <see cref="TriggerManager"/>
    /// wiring. Behaviour mirrors <see cref="SoulWardenFactory.Create(Player)"/>.
    /// </summary>
    public static Creature Create(Player owner) => Create(owner, triggers: null);

    /// <summary>
    /// Construct Soul's Attendant, registering the lifegain trigger with
    /// <paramref name="triggers"/> when supplied. Mirrors
    /// <see cref="SoulWardenFactory.Create(Player, TriggerManager?)"/>.
    /// </summary>
    public static Creature Create(Player owner, TriggerManager? triggers)
    {
        ArgumentNullException.ThrowIfNull(owner);

        var card = new Creature(
            name: CardName,
            manaCost: PrintedManaCost,
            power: Power,
            toughness: Toughness,
            subtypes: new[] { CardSubtype.Human, CardSubtype.Cleric });

        card.SetOwner(owner);
        card.SetController(owner);

        // ----------------------------------------------------------------
        // ETB-other-creature trigger — CR 603.6a / CR 119.3.
        // Identical predicate + effect shape as Soul Warden — see
        // SoulWardenFactory for the canonical commentary.
        // ----------------------------------------------------------------
        var lifegainEffect = new Effect(
            $"{CardName}: controller gains {LifeGainAmount} life",
            () =>
            {
                var controller = card.Controller ?? owner;
                controller.GainLife(LifeGainAmount);
            });

        var etbOtherCreatureTrigger = new TriggeredAbility(
            source: card,
            controller: owner,
            condition: new EventTriggerCondition<CardMovedEvent>((e, _) =>
                e.ToZone == ZoneType.Battlefield
                && e.Card.HasType(CardType.Creature)
                && !ReferenceEquals(e.Card, card)),
            effects: new IEffect[] { lifegainEffect },
            activeZones: new[] { ZoneType.Battlefield });

        card.AddAbility(etbOtherCreatureTrigger);
        triggers?.RegisterTriggeredAbility(etbOtherCreatureTrigger);

        return card;
    }
}
