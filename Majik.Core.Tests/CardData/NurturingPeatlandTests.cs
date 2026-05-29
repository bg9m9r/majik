using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for "Nurturing Peatland" via <see cref="HorizonLandCycleFactory"/>.
///
/// Oracle text (verified against Scryfall, 2026-05-29):
///   {T}, Pay 1 life: Add {B} or {G}.
///   {1}, {T}, Sacrifice this land: Draw a card.
///
/// Mirrors <see cref="HorizonCanopyTests"/> — same painless-dual cycle shape,
/// only the colour pair differs ({B}/{G} instead of {G}/{W}).
///
/// Covers:
/// - Card identity (name, Land type)
/// - Two pay-1-life mana abilities ({B} + {G}) with life-cost activation gate
/// - Life total reduction on mana ability activation (CR 119.4)
/// - {1}, {T}, Sacrifice: Draw a card activated ability shape
/// - Draw effect moves top library card to hand
/// </summary>
public class NurturingPeatlandTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land Peatland(Player owner) =>
        HorizonLandCycleFactory.Create(owner, new[] { "Nurturing Peatland", "B", "G" });

    [Fact]
    public void NurturingPeatland_IsLand()
    {
        Peatland(_alice).HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void NurturingPeatland_NameIsCorrect()
    {
        Peatland(_alice).Name.Should().Be("Nurturing Peatland");
    }

    [Fact]
    public void NurturingPeatland_OwnerAndControllerAreSet()
    {
        var land = Peatland(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NurturingPeatland_HasTwoManaAbilities()
    {
        Peatland(_alice).Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {B} and one for {G}");
    }

    [Fact]
    public void NurturingPeatland_HasBlackManaAbility()
    {
        Peatland(_alice).Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Black == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void NurturingPeatland_HasGreenManaAbility()
    {
        Peatland(_alice).Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Black == 0);
    }

    [Fact]
    public void NurturingPeatland_ManaActivation_ReducesLifeByOne()
    {
        var alice = new Player("Alice", 20);
        var land = Peatland(alice);
        var black = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Black == 1);

        black.Activate();

        alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void NurturingPeatland_CannotActivateManaAt1Life()
    {
        var alice = new Player("Alice", 1);
        var land = Peatland(alice);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeFalse(
            "CR 119.4 — can't pay 1 life from 1 life total");
    }

    [Fact]
    public void NurturingPeatland_SacDrawAbility_HasThreeCosts()
    {
        var ability = Peatland(_alice).Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3, "{1} + tap + sacrifice");
    }

    [Fact]
    public void NurturingPeatland_SacDrawEffect_DrawsTopLibraryCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = Peatland(alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }
}
