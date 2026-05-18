using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Targeting;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

public class TargetLegalityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Spec_AnyTarget_AcceptsCreatureAndPlayer()
    {
        var spec = new TargetSpec("any").AnyCreatureOrPlayer();
        var bear = NewCreature("Bear", _alice);

        TargetLegality.IsLegal(spec, bear, _alice).Should().BeTrue();
        TargetLegality.IsLegal(spec, _bob, _alice).Should().BeTrue();
    }

    [Fact]
    public void Spec_Creatures_RejectsPlayer()
    {
        var spec = new TargetSpec("creature").Creatures();
        TargetLegality.IsLegal(spec, _bob, _alice).Should().BeFalse();
    }

    [Fact]
    public void Hexproof_RejectsOpponentTarget_AllowsOwnerTarget()
    {
        var spec = new TargetSpec("creature").Creatures();
        var bear = NewCreature("Bear", _bob);
        bear.AddAbility(new KeywordAbility("Hexproof", bear, _bob));

        TargetLegality.IsLegal(spec, bear, _alice).Should().BeFalse(); // opponent
        TargetLegality.IsLegal(spec, bear, _bob).Should().BeTrue();    // controller
    }

    [Fact]
    public void Shroud_RejectsAllTargets()
    {
        var spec = new TargetSpec("creature").Creatures();
        var bear = NewCreature("Bear", _bob);
        bear.AddAbility(new KeywordAbility("Shroud", bear, _bob));

        TargetLegality.IsLegal(spec, bear, _alice).Should().BeFalse();
        TargetLegality.IsLegal(spec, bear, _bob).Should().BeFalse();
    }

    [Fact]
    public void ProtectionFromRed_BlocksRedSpell_AllowsNonRed()
    {
        var spec = new TargetSpec("creature").Creatures();
        var bear = NewCreature("Bear", _bob);
        bear.AddAbility(new KeywordAbility("Protection from Red", bear, _bob));

        TargetLegality.IsLegal(spec, bear, _alice, sourceColor: "Red").Should().BeFalse();
        TargetLegality.IsLegal(spec, bear, _alice, sourceColor: "Blue").Should().BeTrue();
    }

    [Fact]
    public void Enumerate_FindsAllLegalTargetsOnBattlefield()
    {
        var spec = new TargetSpec("creature").Creatures();
        var bear1 = NewCreature("Bear1", _alice);
        var bear2 = NewCreature("Bear2", _bob);
        _alice.Zones.Battlefield.AddCard(bear1);
        _bob.Zones.Battlefield.AddCard(bear2);

        var legal = TargetLegality.EnumerateLegal(spec, _alice, new[] { _alice, _bob }).ToList();

        legal.Should().Contain(new object[] { bear1, bear2 });
    }

    [Fact]
    public void Enumerate_AnyCreatureOrPlayer_IncludesBothPlayers()
    {
        var spec = new TargetSpec("any").AnyCreatureOrPlayer();

        var legal = TargetLegality.EnumerateLegal(spec, _alice, new[] { _alice, _bob }).ToList();

        legal.Should().Contain(new object[] { _alice, _bob });
    }

    private static Creature NewCreature(string name, Player owner) =>
        new(name, "1G", 2, 2)
        {
            Owner = owner, Controller = owner, Zone = ZoneType.Battlefield,
        };
}
