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
/// Unit tests for <see cref="VexingBaubleFactory"/>.
///
/// Covers:
/// - Card identity (name, Artifact type)
/// - Owner and controller assignment
/// - Activated ability shape: ManaCostCost({1}) + Tap + Sacrifice
/// - Draw effect: moves top library card to hand
/// - Draw effect: no-op on empty library
/// </summary>
public class VexingBaubleTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void VexingBauble_IsArtifact()
    {
        var bauble = VexingBaubleFactory.Create(_alice);

        bauble.HasType(CardType.Artifact).Should().BeTrue();
    }

    [Fact]
    public void VexingBauble_NameIsCorrect()
    {
        var bauble = VexingBaubleFactory.Create(_alice);

        bauble.Name.Should().Be("Vexing Bauble");
    }

    [Fact]
    public void VexingBauble_OwnerAndControllerAreSet()
    {
        var bauble = VexingBaubleFactory.Create(_alice);

        bauble.Owner.Should().BeSameAs(_alice);
        bauble.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void VexingBauble_HasNoManaAbilities()
    {
        var bauble = VexingBaubleFactory.Create(_alice);

        bauble.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Vexing Bauble produces no mana");
    }

    // -----------------------------------------------------------------------
    // Activated ability: {1}, {T}, Sacrifice: Draw a card
    // -----------------------------------------------------------------------

    [Fact]
    public void VexingBauble_HasExactlyOneActivatedAbility()
    {
        var bauble = VexingBaubleFactory.Create(_alice);

        bauble.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void VexingBauble_DrawAbility_HasExactlyThreeCosts()
    {
        var bauble = VexingBaubleFactory.Create(_alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.Should().HaveCount(3,
            "ManaCostCost({1}) + Tap + Sacrifice");
    }

    [Fact]
    public void VexingBauble_DrawAbility_HasManaCostCostOf1()
    {
        var bauble = VexingBaubleFactory.Create(_alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = ability.Costs.OfType<ManaCostCost>().Single().Cost;

        manaCost.Generic.Should().Be(1, "the {1} component");
        manaCost.White.Should().Be(0);
        manaCost.Blue.Should().Be(0);
        manaCost.Black.Should().Be(0);
        manaCost.Red.Should().Be(0);
        manaCost.Green.Should().Be(0);
    }

    [Fact]
    public void VexingBauble_DrawAbility_HasTapCost()
    {
        var bauble = VexingBaubleFactory.Create(_alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the {T} cost");
    }

    [Fact]
    public void VexingBauble_DrawAbility_HasSacrificeCost()
    {
        var bauble = VexingBaubleFactory.Create(_alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the Sacrifice cost");
    }

    // -----------------------------------------------------------------------
    // Draw effect execution
    // -----------------------------------------------------------------------

    [Fact]
    public void VexingBauble_DrawEffect_MovesTopLibraryCardToHand()
    {
        var alice = new Player("Alice", 20);
        var topCard = new Card("Top Card", "");
        alice.Zones.Library.AddCard(topCard);
        topCard.SetZone(ZoneType.Library);

        var bauble = VexingBaubleFactory.Create(alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().Contain(topCard,
            "draw effect moves the top library card to hand");
        alice.Zones.Library.GetCards().Should().NotContain(topCard,
            "card is removed from the library");
        topCard.Zone.Should().Be(ZoneType.Hand);
    }

    [Fact]
    public void VexingBauble_DrawEffect_OnlyMovesTopCard()
    {
        var alice = new Player("Alice", 20);
        var top = new Card("Top", "");
        var second = new Card("Second", "");
        alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);
        alice.Zones.Library.AddCard(second);
        second.SetZone(ZoneType.Library);

        var bauble = VexingBaubleFactory.Create(alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();
        foreach (var effect in ability.Effects) effect.Execute();

        alice.Zones.Hand.GetCards().Should().ContainSingle()
            .Which.Should().BeSameAs(top, "only the top card is drawn");
        alice.Zones.Library.GetCards().Should().Contain(second,
            "second card is unaffected");
    }

    [Fact]
    public void VexingBauble_DrawEffect_EmptyLibrary_DoesNotThrow()
    {
        var alice = new Player("Alice", 20);
        // Library intentionally empty

        var bauble = VexingBaubleFactory.Create(alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => { foreach (var effect in ability.Effects) effect.Execute(); };

        act.Should().NotThrow("drawing from an empty library is a no-op; SBAs handle loss");
    }

    [Fact]
    public void VexingBauble_DrawAbility_ResolvesWithoutThrowing()
    {
        var alice = new Player("Alice", 20);
        var card = new Card("Some Card", "");
        alice.Zones.Library.AddCard(card);
        card.SetZone(ZoneType.Library);

        var bauble = VexingBaubleFactory.Create(alice);
        var ability = bauble.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => ability.Resolve();

        act.Should().NotThrow();
    }
}
