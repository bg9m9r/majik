using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="LavaclawReachesFactory"/> — Land — Mountain Swamp
/// manland with an X/X animate ability (X = mana paid into {X}{B}{R}).
/// </summary>
public class LavaclawReachesTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void LavaclawReaches_IsLand_MountainSwamp_NoSupertypes()
    {
        var land = LavaclawReachesFactory.Create(_alice);

        land.Name.Should().Be("Lavaclaw Reaches");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Subtypes.Should().Contain(CardSubtype.Mountain);
        land.Subtypes.Should().Contain(CardSubtype.Swamp);
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LavaclawReaches()
    {
        var card = NamedCardFactory.Create("Lavaclaw Reaches", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Lavaclaw Reaches");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(2, "{T}: Add {B} and {T}: Add {R}");
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1, "{X}{B}{R}: animate ability");
    }

    [Fact]
    public void TapForBlack_TapForRed_BothPresent()
    {
        var land = LavaclawReachesFactory.Create(_alice);
        var manaAbilities = land.Abilities.OfType<ManaAbility>().ToList();

        var producedColours = manaAbilities.Select(a =>
        {
            var produced = a.Activate();
            land.Untap(); // reset between probes
            return (produced.Black, produced.Red);
        }).ToList();

        producedColours.Should().Contain(p => p.Black == 1 && p.Red == 0);
        producedColours.Should().Contain(p => p.Black == 0 && p.Red == 1);
    }

    [Fact]
    public void Animate_X_2_ResolvesAsLayer4_PlusLayer7b_WithPower4Toughness2()
    {
        // X = 2 ⇒ body is 2/2 + "gets +2/+0" ⇒ collapsed 4/2 per CR 107.3
        // (see LavaclawReachesBecomesPTEffect xmldoc).
        var effects = new ContinuousEffectsService();
        var land = LavaclawReachesFactory.Create(
            _alice,
            effects,
            replacements: null,
            xValueProvider: () => 2);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<LavaclawReachesAnimateEffect>().SingleOrDefault();
        animateEffect.Should().NotBeNull();
        animateEffect!.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<LavaclawReachesBecomesPTEffect>().SingleOrDefault();
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.ExpiresAtEndOfTurn.Should().BeTrue();
        ptEffect.X.Should().Be(2);
        ptEffect.NewPower.Should().Be(4, "X/X body + X/+0 rider per CR 107.3");
        ptEffect.NewToughness.Should().Be(2);

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "still a land");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Mountain, "printed subtype preserved");
        chars.Subtypes.Should().Contain(CardSubtype.Swamp, "printed subtype preserved");
        chars.Subtypes.Should().Contain(CardSubtype.Elemental);
    }

    [Fact]
    public void Animate_X_0_BodyIs0_0_PerXProvider()
    {
        var effects = new ContinuousEffectsService();
        var land = LavaclawReachesFactory.Create(
            _alice, effects, replacements: null, xValueProvider: () => 0);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<LavaclawReachesBecomesPTEffect>().Single();
        ptEffect.X.Should().Be(0);
        ptEffect.NewPower.Should().Be(0);
        ptEffect.NewToughness.Should().Be(0);
    }

    [Fact]
    public void Animate_EndOfTurnExpiration_Reverts()
    {
        var effects = new ContinuousEffectsService();
        var land = LavaclawReachesFactory.Create(
            _alice, effects, replacements: null, xValueProvider: () => 3);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<LavaclawReachesAnimateEffect>().Should().BeEmpty();
        GetRegisteredEffects(effects).OfType<LavaclawReachesBecomesPTEffect>().Should().BeEmpty();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Elemental);
        // Mountain + Swamp remain (printed subtypes).
        chars.Subtypes.Should().Contain(CardSubtype.Mountain);
        chars.Subtypes.Should().Contain(CardSubtype.Swamp);
    }

    [Fact]
    public void Animate_NoEffectsService_NoOp()
    {
        var land = LavaclawReachesFactory.Create(_alice);
        land.SetZone(ZoneType.Battlefield);
        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();

        var resolve = () => animate.Resolve();
        resolve.Should().NotThrow();
        land.HasType(CardType.Creature).Should().BeFalse();
    }

    private static IEnumerable<ContinuousEffect> GetRegisteredEffects(
        ContinuousEffectsService svc)
    {
        var field = typeof(ContinuousEffectsService).GetField(
            "_effects",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        var list = (System.Collections.IEnumerable)field!.GetValue(svc)!;
        foreach (var e in list) yield return (ContinuousEffect)e;
    }
}
