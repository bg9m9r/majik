using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="BoseijuFactory"/> and <see cref="DiscardSelfCost"/>.
///
/// Covers:
/// - Card identity (name, Legendary supertype, Land type)
/// - Mana ability presence and output
/// - Channel ability cost composition
/// - DiscardSelfCost zone-gating logic
/// </summary>
public class BoseijuTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void Boseiju_IsLegendary()
    {
        var bos = BoseijuFactory.Create(_alice);

        bos.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Boseiju_IsLand()
    {
        var bos = BoseijuFactory.Create(_alice);

        bos.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void Boseiju_OwnerAndControllerAreSet()
    {
        var bos = BoseijuFactory.Create(_alice);

        bos.Owner.Should().BeSameAs(_alice);
        bos.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // {T}: Add {G} mana ability
    // -----------------------------------------------------------------------

    [Fact]
    public void Boseiju_HasExactlyOneManaAbility()
    {
        var bos = BoseijuFactory.Create(_alice);

        bos.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Boseiju_ManaAbility_ProducesGreen()
    {
        var bos = BoseijuFactory.Create(_alice);
        var mana = bos.Abilities.OfType<ManaAbility>().Single();

        mana.ManaGenerated.Green.Should().Be(1, "Boseiju taps for exactly one {G}");
        mana.ManaGenerated.Generic.Should().Be(0, "no colorless component");
    }

    // -----------------------------------------------------------------------
    // Channel ability presence and costs
    // -----------------------------------------------------------------------

    [Fact]
    public void Boseiju_HasExactlyOneChannelAbility()
    {
        var bos = BoseijuFactory.Create(_alice);

        bos.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1,
            "only the Channel ability; mana abilities are ManaAbility, not ActivatedAbility");
    }

    [Fact]
    public void Boseiju_ChannelAbility_HasManaCostCost()
    {
        var bos = BoseijuFactory.Create(_alice);
        var channel = bos.Abilities.OfType<ActivatedAbility>().Single();

        channel.Costs.OfType<ManaCostCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Boseiju_ChannelAbility_ManaCostIs1G()
    {
        var bos = BoseijuFactory.Create(_alice);
        var channel = bos.Abilities.OfType<ActivatedAbility>().Single();
        var manaCost = channel.Costs.OfType<ManaCostCost>().Single().Cost;

        manaCost.Generic.Should().Be(1, "the {1} component");
        manaCost.Green.Should().Be(1, "the {G} component");
    }

    [Fact]
    public void Boseiju_ChannelAbility_HasDiscardSelfCost()
    {
        var bos = BoseijuFactory.Create(_alice);
        var channel = bos.Abilities.OfType<ActivatedAbility>().Single();

        channel.Costs.OfType<DiscardSelfCost>().Should().HaveCount(1);
    }

    [Fact]
    public void Boseiju_ChannelAbility_HasExactlyTwoCosts()
    {
        var bos = BoseijuFactory.Create(_alice);
        var channel = bos.Abilities.OfType<ActivatedAbility>().Single();

        channel.Costs.Should().HaveCount(2, "ManaCostCost({1}{G}) + DiscardSelfCost");
    }

    // -----------------------------------------------------------------------
    // DiscardSelfCost — zone gating
    // -----------------------------------------------------------------------

    [Fact]
    public void DiscardSelfCost_CanPay_WhenCardIsInHand()
    {
        var bos = BoseijuFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(bos);   // Zone.AddCard calls SetZone internally
        var cost = new DiscardSelfCost(bos);

        cost.CanPay(_alice).Should().BeTrue();
    }

    [Fact]
    public void DiscardSelfCost_CannotPay_WhenCardIsOnBattlefield()
    {
        var bos = BoseijuFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(bos);
        var cost = new DiscardSelfCost(bos);

        cost.CanPay(_alice).Should().BeFalse(
            "Channel abilities activate from the Hand zone only (CR 702.74a)");
    }

    [Fact]
    public void DiscardSelfCost_CannotPay_WhenCardIsInGraveyard()
    {
        var bos = BoseijuFactory.Create(_alice);
        _alice.Zones.Graveyard.AddCard(bos);
        var cost = new DiscardSelfCost(bos);

        cost.CanPay(_alice).Should().BeFalse();
    }

    [Fact]
    public void DiscardSelfCost_Pay_MovesCardFromHandToGraveyard()
    {
        var bos = BoseijuFactory.Create(_alice);
        _alice.Zones.Hand.AddCard(bos);
        var cost = new DiscardSelfCost(bos);

        cost.Pay(_alice);

        _alice.Zones.Hand.GetCards().Should().NotContain(bos, "card leaves the hand");
        _alice.Zones.Graveyard.GetCards().Should().Contain(bos, "discarded card goes to graveyard");
        bos.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void DiscardSelfCost_Pay_ThrowsWhenCardNotInHand()
    {
        var bos = BoseijuFactory.Create(_alice);
        // Card is still in Library (default zone) — not in hand
        var cost = new DiscardSelfCost(bos);

        var act = () => cost.Pay(_alice);

        act.Should().Throw<Exception>("cannot discard from a zone other than Hand");
    }

    [Fact]
    public void DiscardSelfCost_CannotPay_WhenCallerIsNotOwner()
    {
        var bob = new Player("Bob", 20);
        var bos = BoseijuFactory.Create(_alice);
        // Put the card in Bob's hand zone, but it is still owned by Alice
        _alice.Zones.Hand.AddCard(bos);
        var cost = new DiscardSelfCost(bos);

        cost.CanPay(bob).Should().BeFalse(
            "only the owner can discard their own card via Channel");
    }

    // -----------------------------------------------------------------------
    // Channel ability resolve (no-op effect — v1)
    // -----------------------------------------------------------------------

    [Fact]
    public void Boseiju_ChannelAbility_ResolvesWithoutThrowing()
    {
        var bos = BoseijuFactory.Create(_alice);
        var channel = bos.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => channel.Resolve();

        act.Should().NotThrow("v1 effect is a no-op stub");
    }

    [Fact]
    public void Boseiju_ChannelAbility_ResolvesWithoutThrowing_WithOpponentsResolver()
    {
        var bob = new Player("Bob", 20);
        var bos = BoseijuFactory.Create(_alice, opponentsResolver: () => new[] { _alice, bob });
        var channel = bos.Abilities.OfType<ActivatedAbility>().Single();

        var act = () => channel.Resolve();

        act.Should().NotThrow();
    }
}
