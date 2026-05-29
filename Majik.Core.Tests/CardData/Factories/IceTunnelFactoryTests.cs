using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="IceTunnelFactory"/> — the U/B snow dual land
/// (Kaldheim). Oracle text:
///   "({T}: Add {U} or {B}.)
///    This land enters tapped."
///
/// Type line: Snow Land — Island Swamp.
///
/// Covers:
/// - Identity: Land type, Snow supertype (CR 205.4d), Island + Swamp
///   land subtypes (CR 205.3i).
/// - Two mana abilities producing {U} and {B} respectively (CR 605.1 —
///   mana abilities don't use the stack).
/// - Dispatcher routing through <see cref="NamedCardFactory"/>.
///
/// "This land enters tapped" (CR 614.1c) is applied on the production load
/// path by <see cref="EntersTappedBinder"/> from the oracle text, not by
/// this factory (same posture as the Guildgate factories).
/// </summary>
public class IceTunnelFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void IceTunnel_Dispatch_ReturnsLandWithCorrectName()
    {
        var card = NamedCardFactory.Create("Ice Tunnel", _alice);

        card.Should().BeAssignableTo<Land>();
        card.Name.Should().Be("Ice Tunnel");
        card.HasType(CardType.Land).Should().BeTrue();
    }

    [Fact]
    public void IceTunnel_HasSnowSupertype()
    {
        var land = (Land)NamedCardFactory.Create("Ice Tunnel", _alice);

        land.HasSupertype(CardSupertype.Snow).Should().BeTrue(
            "Ice Tunnel is a Snow Land (CR 205.4d)");
    }

    [Fact]
    public void IceTunnel_HasIslandAndSwampSubtypes()
    {
        var land = (Land)NamedCardFactory.Create("Ice Tunnel", _alice);

        land.HasSubtype(CardSubtype.Island).Should().BeTrue(
            "Ice Tunnel is an Island land (CR 205.3i)");
        land.HasSubtype(CardSubtype.Swamp).Should().BeTrue(
            "Ice Tunnel is a Swamp land (CR 205.3i)");
    }

    [Fact]
    public void IceTunnel_HasTwoManaAbilities_ProducingBlueAndBlack()
    {
        var land = (Land)NamedCardFactory.Create("Ice Tunnel", _alice);
        var mana = land.Abilities.OfType<ManaAbility>().ToList();

        mana.Should().HaveCount(2, "Ice Tunnel taps for {U} or {B}");
        mana.Should().Contain(m => m.ManaGenerated.Blue == 1);
        mana.Should().Contain(m => m.ManaGenerated.Black == 1);
    }

    [Fact]
    public void IceTunnel_OwnerAndControllerAreSet()
    {
        var land = (Land)NamedCardFactory.Create("Ice Tunnel", _alice);

        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }
}
