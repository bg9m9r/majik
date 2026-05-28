using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Combat;

public class BlockLegalityTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void Flier_CannotBeBlockedByGroundCreature()
    {
        var flier = Make("Drake", "Flying", _alice);
        var ground = Make("Bear", null, _bob);

        BlockLegality.CanBlock(blocker: ground, attacker: flier, out var reason).Should().BeFalse();
        reason.Should().Contain("flying");
    }

    [Fact]
    public void Flier_CanBeBlockedByFlyer()
    {
        var flier = Make("Drake", "Flying", _alice);
        var bird = Make("Bird", "Flying", _bob);

        BlockLegality.CanBlock(bird, flier, out _).Should().BeTrue();
    }

    [Fact]
    public void Flier_CanBeBlockedByReach()
    {
        var flier = Make("Drake", "Flying", _alice);
        var spider = Make("Spider", "Reach", _bob);

        BlockLegality.CanBlock(spider, flier, out _).Should().BeTrue();
    }

    [Fact]
    public void Defender_CannotAttack()
    {
        var wall = Make("Wall", "Defender", _alice);
        BlockLegality.CanAttack(wall, out var reason).Should().BeFalse();
        reason.Should().Contain("defender");
    }

    [Fact]
    public void NonDefender_CanAttack()
    {
        var bear = Make("Bear", null, _alice);
        bear.HasSummoningSickness = false;
        BlockLegality.CanAttack(bear, out _).Should().BeTrue();
    }

    [Fact]
    public void SummoningSickness_CannotAttack()
    {
        var bear = Make("Bear", null, _alice); // ctor leaves sickness true
        BlockLegality.CanAttack(bear, out var reason).Should().BeFalse();
        reason.Should().Contain("summoning sickness");
    }

    [Fact]
    public void SummoningSickness_WithHaste_CanAttack()
    {
        var bear = Make("Bear", "Haste", _alice);
        BlockLegality.CanAttack(bear, out _).Should().BeTrue();
    }

    [Fact]
    public void Menace_RequiresAtLeastTwoBlockers()
    {
        var menaceAttacker = Make("Menacer", "Menace", _alice);
        var blocker = Make("Bear", null, _bob);

        BlockLegality.MenaceSatisfied(menaceAttacker, blockerCount: 1).Should().BeFalse();
        BlockLegality.MenaceSatisfied(menaceAttacker, blockerCount: 2).Should().BeTrue();
        // Non-menace attackers always satisfied
        BlockLegality.MenaceSatisfied(blocker, blockerCount: 1).Should().BeTrue();
    }

    [Fact]
    public void MinBlockers_ThreeOrMore_RequiresAtLeastThreeBlockers()
    {
        // Troll of Khazad-dûm: can't be blocked except by three or more
        var troll = TrollOfKhazadDumFactory.Create(_alice);

        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 0).Should().BeTrue(
            "unblocked is always legal (CR 509.1b)");
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 1).Should().BeFalse();
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 2).Should().BeFalse();
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 3).Should().BeTrue();
        BlockLegality.MinBlockersSatisfied(troll, blockerCount: 4).Should().BeTrue();
    }

    [Fact]
    public void MinBlockers_PlainCreature_AlwaysSatisfied()
    {
        var bear = Make("Bear", null, _alice);
        BlockLegality.MinBlockersSatisfied(bear, blockerCount: 1).Should().BeTrue();
    }

    private static Creature Make(string name, string? keyword, Player owner)
    {
        var c = new Creature(name, "1", 2, 2) { Owner = owner, Controller = owner };
        if (keyword != null) c.AddAbility(new KeywordAbility(keyword, c, owner));
        return c;
    }
}
