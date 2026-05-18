using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Spells;

namespace Majik.Core.Keywords;

/// <summary>
/// CR 702.108 — Prowess: "Whenever you cast a noncreature spell, this
/// creature gets +1/+1 until end of turn."
///
/// Built as a <see cref="TriggeredAbility"/> over <see cref="SpellCastEvent"/>;
/// effect registers a one-turn pump on the <see cref="ContinuousEffectsService"/>.
/// </summary>
public static class ProwessFactory
{
    public static TriggeredAbility Build(Creature source, ContinuousEffectsService effects)
    {
        if (source == null) throw new ArgumentNullException(nameof(source));
        if (effects == null) throw new ArgumentNullException(nameof(effects));

        var condition = new EventTriggerCondition<SpellCastEvent>((e, _) =>
            ReferenceEquals(e.Spell.Controller, source.Controller)
            && !e.Spell.Card.HasType(CardType.Creature));

        var pump = new Effect("prowess +1/+1 until end of turn", () =>
        {
            effects.Register(new ProwessPumpEffect(source));
        });

        return new TriggeredAbility(source, source.Controller!, condition, effects: new[] { pump });
    }

    private sealed class ProwessPumpEffect : ContinuousEffect
    {
        private readonly Creature _target;
        public ProwessPumpEffect(Creature t) { _target = t; }
        public override Layer Layer => Layer.PT_Modify;
        public override bool ExpiresAtEndOfTurn => true;
        public override bool AppliesTo(Creature c) => ReferenceEquals(c, _target);
        public override void Apply(CreatureCharacteristics chars)
        {
            chars.Power += 1;
            chars.Toughness += 1;
        }
    }
}
