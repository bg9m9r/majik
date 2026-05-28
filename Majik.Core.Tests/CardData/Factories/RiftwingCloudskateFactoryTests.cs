using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="RiftwingCloudskateFactory"/>.
///
/// Covers:
/// - Identity (Creature, Illusion, 2/2, {3}{U}{U}, owner / controller).
/// - Flying + Suspend keyword markers attached.
/// - NamedCardFactory dispatch.
/// - ETB trigger shape — single 1..1 "target permanent" request.
/// - ETB resolve bounces opponent's permanent to its owner's hand
///   (CR 701.10).
/// - ETB resolve guards against post-cast moves (CR 608.2b).
/// - ETB resolve with no targets short-circuits.
/// </summary>
public class RiftwingCloudskateFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void RiftwingCloudskate_Identity()
    {
        var c = RiftwingCloudskateFactory.Create(_alice);

        c.Name.Should().Be("Riftwing Cloudskate");
        c.ManaCost.Should().Be("{3}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Illusion).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RiftwingCloudskate_HasFlyingAndSuspendMarkers()
    {
        var c = RiftwingCloudskateFactory.Create(_alice);
        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();

        keywords.Should().Contain("Flying");
        keywords.Should().Contain("Suspend",
            "Suspend mechanic is deferred — keyword marker still attached for oracle audits");
    }

    [Fact]
    public void RiftwingCloudskate_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Riftwing Cloudskate", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Riftwing Cloudskate");
        ((Creature)c).BasePower.Should().Be(2);
        ((Creature)c).BaseToughness.Should().Be(2);
    }

    [Fact]
    public void RiftwingCloudskate_EtbTrigger_HasSinglePermanentTarget()
    {
        var c = RiftwingCloudskateFactory.Create(_alice);

        var etb = c.Abilities.OfType<TriggeredAbility>().Single();
        etb.ActiveZones.Should().Contain(ZoneType.Battlefield);
        etb.TargetRequests.Should().HaveCount(1);

        var req = etb.TargetRequests[0];
        req.MinTargets.Should().Be(1);
        req.MaxTargets.Should().Be(1);
        req.Description.Should().Contain("permanent");
        req.Intent.Should().Be(BotIntent.Bounce);
    }

    [Fact]
    public void RiftwingCloudskate_Etb_BouncesTargetPermanentToOwnersHand()
    {
        var cloudskate = RiftwingCloudskateFactory.Create(_alice);
        cloudskate.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cloudskate);

        // Bob has a permanent we want to bounce.
        var target = new Creature("Goblin Guide", "{R}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        target.SetZone(ZoneType.Battlefield);
        _bob.Zones.Battlefield.AddCard(target);

        var etb = cloudskate.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var e in etb.Effects) e.Execute();

        target.Zone.Should().Be(ZoneType.Hand,
            "ETB bounces the targeted permanent to its owner's hand (CR 701.10)");
        _bob.Zones.Hand.GetCards().Should().Contain(target);
        _bob.Zones.Battlefield.GetCards().Should().NotContain(target);
    }

    [Fact]
    public void RiftwingCloudskate_Etb_OffBattlefieldTarget_NoOp()
    {
        // CR 608.2b — if the target has already left the battlefield by
        // resolution, the effect does nothing.
        var cloudskate = RiftwingCloudskateFactory.Create(_alice);
        cloudskate.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cloudskate);

        var target = new Creature("Bob's Bear", "{1}{G}", 2, 2);
        target.SetOwner(_bob);
        target.SetController(_bob);
        // Note: NOT on the battlefield.
        target.SetZone(ZoneType.Graveyard);
        _bob.Zones.Graveyard.AddCard(target);

        var etb = cloudskate.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { target },
        });
        foreach (var e in etb.Effects) e.Execute();

        target.Zone.Should().Be(ZoneType.Graveyard,
            "illegal-on-resolution target — bounce no-ops (CR 608.2b)");
        _bob.Zones.Hand.GetCards().Should().NotContain(target);
    }

    [Fact]
    public void RiftwingCloudskate_Etb_NoTargets_NoOp()
    {
        var cloudskate = RiftwingCloudskateFactory.Create(_alice);
        cloudskate.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(cloudskate);

        var etb = cloudskate.Abilities.OfType<TriggeredAbility>().Single();
        etb.SetChosenTargets(System.Array.Empty<IReadOnlyList<object>>());

        // No targets chosen → resolve-time no-op. Contract: must not throw.
        var act = () => { foreach (var e in etb.Effects) e.Execute(); };
        act.Should().NotThrow();
    }
}
