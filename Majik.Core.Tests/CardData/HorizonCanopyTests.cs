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
/// Unit tests for "Horizon Canopy" via <see cref="HorizonLandCycleFactory"/>.
///
/// Oracle text (verified against Scryfall, 2026-05-28):
///   {T}, Pay 1 life: Add {G} or {W}.
///   {1}, {T}, Sacrifice this land: Draw a card.
///
/// Covers:
/// - Card identity (name, Land type)
/// - Two pay-1-life mana abilities ({G} + {W}) with life-cost activation gate
/// - Life total reduction on mana ability activation
/// - {1}, {T}, Sacrifice: Draw a card activated ability shape
/// - Draw effect moves top library card to hand
/// </summary>
public class HorizonCanopyTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land Canopy(Player owner) =>
        HorizonLandCycleFactory.Create(owner, new[] { "Horizon Canopy", "G", "W" });

    [Fact]
    public void HorizonCanopy_IsLand()
    {
        Canopy(_alice).HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void HorizonCanopy_NameIsCorrect()
    {
        Canopy(_alice).Name.Should().Be("Horizon Canopy");
    }

    [Fact]
    public void HorizonCanopy_OwnerAndControllerAreSet()
    {
        var land = Canopy(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void HorizonCanopy_HasTwoManaAbilities()
    {
        Canopy(_alice).Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {G} and one for {W}");
    }

    [Fact]
    public void HorizonCanopy_HasGreenManaAbility()
    {
        Canopy(_alice).Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.White == 0);
    }

    [Fact]
    public void HorizonCanopy_HasWhiteManaAbility()
    {
        Canopy(_alice).Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.White == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void HorizonCanopy_ManaActivation_ReducesLifeByOne()
    {
        var alice = new Player("Alice", 20);
        var land = Canopy(alice);
        var white = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.White == 1);

        white.Activate();

        alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void HorizonCanopy_CannotActivateManaAt1Life()
    {
        var alice = new Player("Alice", 1);
        var land = Canopy(alice);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeFalse(
            "CR 119.4 — can't pay 1 life from 1 life total");
    }

    [Fact]
    public void HorizonCanopy_SacDrawAbility_HasThreeCosts()
    {
        var ability = Canopy(_alice).Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3, "{1} + tap + sacrifice");
    }

    [Fact]
    public void HorizonCanopy_SacDrawEffect_DrawsTopLibraryCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = Canopy(alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }
}
