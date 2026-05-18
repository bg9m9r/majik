using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Zones;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.140 — Landfall: "Whenever a land you control enters the
/// battlefield, [effect]." Built atop the existing trigger framework as
/// a parameterised <see cref="TriggeredAbility"/> factory; the actual
/// effect (gain life, deal damage, etc.) is supplied by the caller.
/// </summary>
public static class LandfallFactory
{
    public static TriggeredAbility Build(
        ICard source,
        IEnumerable<IEffect> effects)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (source.Controller == null)
            throw new InvalidOperationException("Landfall source must have a controller");

        var condition = new EventTriggerCondition<CardMovedEvent>((e, _) =>
            e.ToZone == ZoneType.Battlefield
            && e.Card.HasType(CardType.Land)
            && ReferenceEquals(e.Card.Controller, source.Controller));

        return new TriggeredAbility(source, source.Controller, condition, effects: effects);
    }
}
