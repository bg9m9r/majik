using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Counters;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

/// <summary>
/// Tests for CR 701.49 — Amass [tribe] N keyword action.
/// </summary>
public class AmassActionTests
{
    [Fact]
    public void Apply_NoArmyOnBattlefield_CreatesOrcArmyToken_With1Counter()
    {
        var alice = new Player("Alice", 20);

        var army = AmassAction.Apply(alice, 1, CardSubtype.Orc);

        army.IsToken.Should().BeTrue();
        army.Subtypes.Should().Contain(CardSubtype.Army);
        army.Subtypes.Should().Contain(CardSubtype.Orc);
        army.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
        army.Zone.Should().Be(ZoneType.Battlefield);
        alice.Zones.Battlefield.GetCards().Should().Contain(army);
    }

    [Fact]
    public void Apply_ExistingArmy_AddsCountersToIt_DoesNotCreateToken()
    {
        var alice = new Player("Alice", 20);
        var existing = new Creature("Some Army", "", 0, 0,
            subtypes: new[] { CardSubtype.Army })
        {
            Owner = alice,
            Controller = alice,
        };
        existing.SetZone(ZoneType.Battlefield);
        alice.Zones.Battlefield.AddCard(existing);

        var result = AmassAction.Apply(alice, 2, CardSubtype.Orc);

        result.Should().BeSameAs(existing);
        existing.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
        // No second creature added to battlefield
        alice.Zones.Battlefield.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Apply_AmassThreeFromScratch_CreatesTokenWith3Counters()
    {
        var alice = new Player("Alice", 20);

        var army = AmassAction.Apply(alice, 3, CardSubtype.Goblin);

        army.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
        army.Subtypes.Should().Contain(CardSubtype.Army);
        army.Subtypes.Should().Contain(CardSubtype.Goblin);
    }

    [Fact]
    public void Apply_ZeroCount_Throws()
    {
        var alice = new Player("Alice", 20);

        Action act = () => AmassAction.Apply(alice, 0, CardSubtype.Orc);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Apply_NegativeCount_Throws()
    {
        var alice = new Player("Alice", 20);

        Action act = () => AmassAction.Apply(alice, -1, CardSubtype.Orc);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Apply_TribeIsArmy_TokenHasOnlyArmySubtype()
    {
        // If caller passes CardSubtype.Army as tribe, don't duplicate it.
        var alice = new Player("Alice", 20);

        var army = AmassAction.Apply(alice, 1, CardSubtype.Army);

        army.Subtypes.Should().ContainSingle(s => s == CardSubtype.Army);
    }

    [Fact]
    public void Apply_SecondAmass_StacksCountersOnSameArmy()
    {
        var alice = new Player("Alice", 20);

        // First Amass creates the token.
        var first = AmassAction.Apply(alice, 1, CardSubtype.Orc);
        // Second Amass should find the token and add more counters.
        var second = AmassAction.Apply(alice, 2, CardSubtype.Orc);

        second.Should().BeSameAs(first);
        first.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
        alice.Zones.Battlefield.GetCards().Should().HaveCount(1);
    }

    [Fact]
    public void Apply_NullController_Throws()
    {
        Action act = () => AmassAction.Apply(null!, 1, CardSubtype.Orc);

        act.Should().Throw<ArgumentNullException>();
    }
}
