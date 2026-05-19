using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;

namespace Majik.Core.Abilities;

/// <summary>
/// Static factory of common <see cref="ITriggerCondition"/> instances.
/// Composes <see cref="EventTriggerCondition{TEvent}"/> with predicates that
/// encode common Magic trigger phrases ("when X enters", "when X dies", ...).
/// </summary>
public static class Triggers
{
    /// <summary>
    /// "When ~ enters the battlefield" — fires when the given source card moves
    /// to the battlefield.
    /// </summary>
    public static ITriggerCondition OnEnterBattlefieldSelf(ICard source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, source) && e.ToZone == ZoneType.Battlefield);
    }

    /// <summary>
    /// "Whenever a creature enters the battlefield" — fires for any creature entering.
    /// </summary>
    public static ITriggerCondition OnAnyCreatureEntersBattlefield()
    {
        return new EventTriggerCondition<CardMovedEvent>(
            (e, _) => e.ToZone == ZoneType.Battlefield && e.Card.HasType(CardType.Creature));
    }

    /// <summary>
    /// "When ~ dies" — creature moving from battlefield to graveyard (Rule 700.4).
    /// </summary>
    public static ITriggerCondition OnDies(ICard source)
    {
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        return new EventTriggerCondition<CardMovedEvent>(
            (e, _) => ReferenceEquals(e.Card, source)
                      && e.FromZone == ZoneType.Battlefield
                      && e.ToZone == ZoneType.Graveyard);
    }

    /// <summary>
    /// "Whenever PLAYER draws a card" — fires when the given player draws.
    /// </summary>
    public static ITriggerCondition OnCardDrawnByPlayer(Player player)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        return new EventTriggerCondition<CardDrawnEvent>(
            (e, _) => ReferenceEquals(e.Player, player));
    }

    /// <summary>
    /// "Whenever a player casts a spell" — fires on any spell cast.
    /// </summary>
    public static ITriggerCondition OnSpellCast()
    {
        return new EventTriggerCondition<SpellCastEvent>((_, _) => true);
    }

    /// <summary>
    /// CR 702.50 — Prowess. "Whenever you cast a noncreature spell, this
    /// gets +1/+1 until end of turn." Fires on SpellCastEvent where the
    /// spell's controller is <paramref name="controller"/> AND the spell
    /// is non-creature.
    /// </summary>
    public static ITriggerCondition OnNonCreatureSpellCastByController(Player controller)
    {
        return new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, controller)
            && !e.Spell.Card.HasType(Majik.Core.Cards.Types.CardType.Creature));
    }
}
