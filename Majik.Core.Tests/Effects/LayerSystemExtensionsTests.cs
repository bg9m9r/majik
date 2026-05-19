using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class LayerSystemExtensionsTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ---------- Layer 1: Copy ----------

    [Fact]
    public void CopyEffect_CopiesBasePT()
    {
        var svc = new ContinuousEffectsService();
        var hillGiant = new Creature("Hill Giant", "3R", 3, 3,
            subtypes: new[] { CardSubtype.Beast })
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        var clone = new Creature("Clone", "3U", 0, 0)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        svc.Register(new CopyEffect(clone, hillGiant));

        clone.Power.Should().Be(3);
        clone.Toughness.Should().Be(3);
    }

    [Fact]
    public void CopyEffect_PrecedesPumpAt7c()
    {
        var svc = new ContinuousEffectsService();
        var hillGiant = new Creature("Hill Giant", "3R", 3, 3)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        var clone = new Creature("Clone", "3U", 0, 0)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };
        svc.Register(new CopyEffect(clone, hillGiant));
        // +1/+1 counter — applied last at 7c.
        clone.Counters.Add(Majik.Core.Counters.CounterType.PlusOnePlusOne);

        clone.Power.Should().Be(4);
        clone.Toughness.Should().Be(4);
    }

    // ---------- Layer 2: Control change ----------

    [Fact]
    public void ControlChangeEffect_RemapsController()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        svc.Register(new ControlChangeEffect(bear, _bob));

        svc.EffectiveController(bear).Should().BeSameAs(_bob);
    }

    [Fact]
    public void ControlChangeEffect_Inactive_FallsBackToBaseController()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2)
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        var swap = new ControlChangeEffect(bear, _bob);
        svc.Register(swap);
        svc.EffectiveController(bear).Should().BeSameAs(_bob);

        bear.Zone = ZoneType.Graveyard; // effect's IsActive checks zone
        svc.EffectiveController(bear).Should().BeSameAs(_alice);
    }

    // ---------- Layer 4: Type-add ----------

    [Fact]
    public void TypeAddEffect_AddsSubtype()
    {
        var svc = new ContinuousEffectsService();
        var bear = new Creature("Bear", "1G", 2, 2,
            subtypes: new[] { CardSubtype.Beast })
        { Owner = _alice, Controller = _alice, Zone = ZoneType.Battlefield, ActiveEffects = svc };

        svc.Register(new AddSubtypeEffect(bear, CardSubtype.Goblin));

        var chars = svc.Compute(bear);
        chars.Subtypes.Should().Contain(CardSubtype.Goblin);
        chars.Subtypes.Should().Contain(CardSubtype.Beast);
    }
}
