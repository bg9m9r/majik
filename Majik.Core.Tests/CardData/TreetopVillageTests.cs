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
/// Tests for <see cref="TreetopVillageFactory"/> — Land manland.
/// </summary>
public class TreetopVillageTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void TreetopVillage_IsLand_NoSubtypes_NoSupertypes()
    {
        var land = TreetopVillageFactory.Create(_alice);

        land.Name.Should().Be("Treetop Village");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasType(CardType.Creature).Should().BeFalse();
        land.Subtypes.Should().BeEmpty();
        land.Supertypes.Should().BeEmpty();
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_TreetopVillage()
    {
        var card = NamedCardFactory.Create("Treetop Village", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Treetop Village");
        card.HasType(CardType.Land).Should().BeTrue();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Should().HaveCount(1);
    }

    [Fact]
    public void TreetopVillage_TapForGreen()
    {
        var land = TreetopVillageFactory.Create(_alice);
        var mana = land.Abilities.OfType<ManaAbility>().Single();
        var produced = mana.Activate();

        produced.Green.Should().Be(1);
        produced.Generic.Should().Be(0);
        land.IsTapped.Should().BeTrue();
    }

    [Fact]
    public void Animate_RegistersLayer4AndLayer7b_EotExpiring()
    {
        var effects = new ContinuousEffectsService();
        var land = TreetopVillageFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        var animateEffect = GetRegisteredEffects(effects)
            .OfType<TreetopVillageAnimateEffect>().SingleOrDefault();
        animateEffect.Should().NotBeNull();
        animateEffect!.Target.Should().BeSameAs(land);
        animateEffect.Layer.Should().Be(Layer.Type);
        animateEffect.ExpiresAtEndOfTurn.Should().BeTrue();

        var ptEffect = GetRegisteredEffects(effects)
            .OfType<TreetopVillageBecomesPTEffect>().SingleOrDefault();
        ptEffect.Should().NotBeNull();
        ptEffect!.Layer.Should().Be(Layer.PT_SetBase);
        ptEffect.NewPower.Should().Be(3);
        ptEffect.NewToughness.Should().Be(3);

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land, "still a land");
        chars.Types.Should().Contain(CardType.Creature);
        chars.Subtypes.Should().Contain(CardSubtype.Ape);
        chars.Keywords.Should().Contain("Trample");
    }

    [Fact]
    public void Animate_EndOfTurnExpiration_Reverts()
    {
        var effects = new ContinuousEffectsService();
        var land = TreetopVillageFactory.Create(_alice, effects, replacements: null);
        land.SetZone(ZoneType.Battlefield);

        var animate = land.Abilities
            .Where(a => a.GetType() == typeof(ActivatedAbility))
            .Cast<ActivatedAbility>()
            .Single();
        animate.Resolve();

        effects.ExpireEndOfTurn();

        GetRegisteredEffects(effects).OfType<TreetopVillageAnimateEffect>().Should().BeEmpty();
        GetRegisteredEffects(effects).OfType<TreetopVillageBecomesPTEffect>().Should().BeEmpty();

        var chars = effects.Compute(land);
        chars.Types.Should().Contain(CardType.Land);
        chars.Types.Should().NotContain(CardType.Creature);
        chars.Subtypes.Should().NotContain(CardSubtype.Ape);
        chars.Keywords.Should().NotContain("Trample");
    }

    [Fact]
    public void Animate_NoEffectsService_NoOp()
    {
        var land = TreetopVillageFactory.Create(_alice);
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
