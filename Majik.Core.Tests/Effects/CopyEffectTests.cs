using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Combat;
using Majik.Core.Counters;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class CopyEffectTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Clone_CopiesPT()
    {
        var svc = new ContinuousEffectsService();
        var original = new Creature("Hill Giant", "3R", 3, 3);
        var clone = new Creature("Clone", "3U", 0, 0) { ActiveEffects = svc };

        svc.Register(new CopyEffect(clone, original));

        clone.Power.Should().Be(3);
        clone.Toughness.Should().Be(3);
    }

    [Fact]
    public void Clone_CopiesPrintedKeywords()
    {
        var svc = new ContinuousEffectsService();
        var original = new Creature("Air Elemental", "3UU", 4, 4) { Owner = _alice };
        original.AddAbility(new KeywordAbility("Flying", original, _alice));

        var clone = new Creature("Clone", "3U", 0, 0) { ActiveEffects = svc };
        svc.Register(new CopyEffect(clone, original));

        clone.Power.Should().Be(4);
        CombatAbilities.HasFlying(clone).Should().BeTrue();
    }

    [Fact]
    public void Clone_LaterLayersApplyOnTop()
    {
        var svc = new ContinuousEffectsService();
        var original = new Creature("Bear", "1G", 2, 2);
        var clone = new Creature("Clone", "3U", 0, 0) { ActiveEffects = svc };
        clone.Counters.Add(CounterType.PlusOnePlusOne, 2);

        svc.Register(new CopyEffect(clone, original));

        clone.Power.Should().Be(4); // copied 2 + 2 counters
    }
}
