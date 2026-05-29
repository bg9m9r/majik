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
/// Unit tests for "Waterlogged Grove" via <see cref="HorizonLandCycleFactory"/>.
///
/// Oracle text (verified against Scryfall, 2026-05-29):
///   {T}, Pay 1 life: Add {G} or {U}.
///   {1}, {T}, Sacrifice this land: Draw a card.
///
/// Waterlogged Grove is the {G}/{U} member of the Modern Horizons
/// "Horizon Canopy" painless-dual cycle. It shares the cycle's two ability
/// shapes (pay-life dual mana + sac-to-draw), so the parametric
/// <see cref="HorizonLandCycleFactory"/> handles it with a colour-pair arg.
///
/// Covers:
/// - Card identity (name, Land type)
/// - Two pay-1-life mana abilities ({G} + {U}) with life-cost activation gate
///   (CR 119.4 — can't pay 1 life from 1 life total)
/// - Life total reduction on mana ability activation
/// - {1}, {T}, Sacrifice: Draw a card activated ability shape
/// - Draw effect moves top library card to hand
/// </summary>
public class WaterloggedGroveTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Land Grove(Player owner) =>
        HorizonLandCycleFactory.Create(owner, new[] { "Waterlogged Grove", "G", "U" });

    [Fact]
    public void WaterloggedGrove_IsLand()
    {
        Grove(_alice).HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void WaterloggedGrove_NameIsCorrect()
    {
        Grove(_alice).Name.Should().Be("Waterlogged Grove");
    }

    [Fact]
    public void WaterloggedGrove_OwnerAndControllerAreSet()
    {
        var land = Grove(_alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WaterloggedGrove_HasTwoManaAbilities()
    {
        Grove(_alice).Abilities.OfType<ManaAbility>().Should().HaveCount(2,
            "one for {G} and one for {U}");
    }

    [Fact]
    public void WaterloggedGrove_HasGreenManaAbility()
    {
        Grove(_alice).Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Green == 1 && m.ManaGenerated.Blue == 0);
    }

    [Fact]
    public void WaterloggedGrove_HasBlueManaAbility()
    {
        Grove(_alice).Abilities.OfType<ManaAbility>()
            .Should().ContainSingle(m => m.ManaGenerated.Blue == 1 && m.ManaGenerated.Green == 0);
    }

    [Fact]
    public void WaterloggedGrove_ManaActivation_ReducesLifeByOne()
    {
        var alice = new Player("Alice", 20);
        var land = Grove(alice);
        var blue = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Blue == 1);

        blue.Activate();

        alice.LifeTotal.Should().Be(19);
    }

    [Fact]
    public void WaterloggedGrove_CannotActivateManaAt1Life()
    {
        var alice = new Player("Alice", 1);
        var land = Grove(alice);
        var green = land.Abilities.OfType<ManaAbility>().Single(m => m.ManaGenerated.Green == 1);

        green.CanActivate().Should().BeFalse(
            "CR 119.4 — can't pay 1 life from 1 life total");
    }

    [Fact]
    public void WaterloggedGrove_SacDrawAbility_HasThreeCosts()
    {
        var ability = Grove(_alice).Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3, "{1} + tap + sacrifice");
    }

    [Fact]
    public void WaterloggedGrove_SacDrawEffect_DrawsTopLibraryCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        var land = Grove(alice);
        var ability = land.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(top);
        top.Zone.Should().Be(ZoneType.Hand);
    }
}
