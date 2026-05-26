using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Combat;
using Majik.Core.Effects;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.Effects;

/// <summary>
/// CR 509.1b — covers <see cref="CantBeBlockedExceptByEffect"/> + its
/// <see cref="BlockLegality.CanBlock"/> integration. Uses
/// <see cref="SignalPestFactory"/> as the primary regression case since
/// "can't be blocked except by flying or reach" is the canonical shape;
/// also exercises the multi-restriction intersection path.
/// </summary>
public class CantBeBlockedExceptByEffectTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void SignalPest_CannotBeBlockedByGroundCreature()
    {
        var effects = new ContinuousEffectsService();
        var pest = SignalPestFactory.Create(_alice, effects);
        var ground = Make("Bear", null, _bob);

        BlockLegality.CanBlock(ground, pest, out var reason).Should().BeFalse();
        reason.Should().Contain("except by");
    }

    [Fact]
    public void SignalPest_CanBeBlockedByFlyer()
    {
        var effects = new ContinuousEffectsService();
        var pest = SignalPestFactory.Create(_alice, effects);
        var bird = Make("Bird", "Flying", _bob);

        BlockLegality.CanBlock(bird, pest, out _).Should().BeTrue();
    }

    [Fact]
    public void SignalPest_CanBeBlockedByReach()
    {
        var effects = new ContinuousEffectsService();
        var pest = SignalPestFactory.Create(_alice, effects);
        var spider = Make("Spider", "Reach", _bob);

        BlockLegality.CanBlock(spider, pest, out _).Should().BeTrue();
    }

    [Fact]
    public void PlainCreature_StillBlockableNormally()
    {
        // Regression: a creature without any CantBeBlockedExceptByEffect must
        // not be affected by the new code path.
        var attacker = Make("Grizzly Bears", null, _alice);
        var blocker = Make("Bear Cub", null, _bob);

        BlockLegality.CanBlock(blocker, attacker, out _).Should().BeTrue();
    }

    [Fact]
    public void TwoRestrictions_Stack_BothMustAllow()
    {
        // Attacker has two CantBeBlockedExceptByEffect restrictions:
        //   - blockers must have Flying
        //   - blockers must have Trample
        // Predicates intersect — only a Flying+Trample blocker can block.
        var effects = new ContinuousEffectsService();
        var attacker = MakeWithEffects("Hybrid Attacker", null, _alice, effects);

        effects.Register(new CantBeBlockedExceptByEffect(
            attacker, b => b is Creature c && CombatAbilities.HasFlying(c)));
        effects.Register(new CantBeBlockedExceptByEffect(
            attacker, b => b is Creature c && CombatAbilities.HasTrample(c)));

        var flyerOnly = Make("Drake", "Flying", _bob);
        var trampleOnly = Make("Rhino", "Trample", _bob);
        var both = MakeMulti("Dragon", new[] { "Flying", "Trample" }, _bob);

        BlockLegality.CanBlock(flyerOnly, attacker, out _).Should().BeFalse();
        BlockLegality.CanBlock(trampleOnly, attacker, out _).Should().BeFalse();
        BlockLegality.CanBlock(both, attacker, out _).Should().BeTrue();
    }

    [Fact]
    public void SignalPest_FactoryDoesNotWireRestriction_WhenNoEffectsService()
    {
        // Shape-only overload: no ActiveEffects → no restriction → any
        // untapped creature can block (vanilla 509.1a fallback).
        var pest = SignalPestFactory.Create(_alice);
        var ground = Make("Bear", null, _bob);

        BlockLegality.CanBlock(ground, pest, out _).Should().BeTrue();
    }

    private static Creature Make(string name, string? keyword, Player owner)
    {
        var c = new Creature(name, "1", 2, 2) { Owner = owner, Controller = owner };
        if (keyword != null) c.AddAbility(new KeywordAbility(keyword, c, owner));
        return c;
    }

    private static Creature MakeMulti(string name, string[] keywords, Player owner)
    {
        var c = new Creature(name, "1", 2, 2) { Owner = owner, Controller = owner };
        foreach (var k in keywords) c.AddAbility(new KeywordAbility(k, c, owner));
        return c;
    }

    private static Creature MakeWithEffects(
        string name, string? keyword, Player owner, ContinuousEffectsService effects)
    {
        var c = Make(name, keyword, owner);
        c.ActiveEffects = effects;
        return c;
    }
}
