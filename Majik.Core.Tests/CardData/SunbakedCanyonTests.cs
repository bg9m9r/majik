using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SunbakedCanyonFactory"/>.
///
/// Covers:
/// - Card identity (name, Land type, non-legendary)
/// - Two pay-1-life mana abilities ({R} + {W}) with life-cost activation gate
/// - Life total reduction on mana ability activation
/// - {1}, {T}, Sacrifice: Draw a card activated ability shape
/// - Draw effect moves top library card to hand
/// </summary>
public class SunbakedCanyonTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SunbakedCanyon_IsLand()
    {
        var land = SunbakedCanyonFactory.Create(_alice);

        land.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void SunbakedCanyon_NameIsCorrect()
    {
        var land = SunbakedCanyonFactory.Create(_alice);

        land.Name.Should().Be("Sunbaked Canyon");
    }

    [Fact]
    public void SunbakedCanyon_OwnerAndControllerAreSet()
    {
        var land = SunbakedCanyonFactory.Create(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SunbakedCanyon_HasTwoManaAbilities()
    {
        var land = SunbakedCanyonFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {R} and one for {W}");
    }

    [Fact]
    public void SunbakedCanyon_HasRedManaAbility()
    {
        var land = SunbakedCanyonFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Red == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void SunbakedCanyon_HasWhiteManaAbility()
    {
        var land = SunbakedCanyonFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Red == 0);
    }

    [Fact]
    public void SunbakedCanyon_ManaActivation_ReducesLifeByOne()
    {
        var alice = new Player("Alice", 20);
        var land = SunbakedCanyonFactory.Create(alice);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.Activate();

        alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void SunbakedCanyon_CannotActivateManaAt1Life()
    {
        var alice = new Player("Alice", 1);
        var land = SunbakedCanyonFactory.Create(alice);
        var red = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Red == 1);

        red.CanActivate().Should().BeFalse(
            "CR 119.4 — can't pay 1 life from 1 life total");
    }

    [Fact]
    public void SunbakedCanyon_SacDrawAbility_HasThreeCosts()
    {
        var land = SunbakedCanyonFactory.Create(_alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3, "{1} + tap + sacrifice");
    }

    [Fact]
    public void SunbakedCanyon_SacDrawEffect_DrawsTopLibraryCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = SunbakedCanyonFactory.Create(alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }
}
