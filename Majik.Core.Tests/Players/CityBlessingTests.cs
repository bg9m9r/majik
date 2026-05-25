using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Domain.DomainEvents;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Players;

/// <summary>
/// CR 702.131 — Ascend / city's blessing. Once a player has controlled 10
/// or more permanents at the same time, that player has the city's
/// blessing for the rest of the game. The state is permanent (CR 702.131c
/// — never lost), and "if you have the city's blessing" conditional
/// effects key off <see cref="Player.HasCitysBlessing"/>.
/// </summary>
public class CityBlessingTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Creature MakeDummyPermanent(Player owner, string name = "Dummy")
    {
        var c = new Creature(name, "{0}", 1, 1)
        {
            Owner = owner,
            Controller = owner,
        };
        c.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(c);
        return c;
    }

    // ------------------------------------------------------------------
    // CR 702.131 — threshold semantics
    // ------------------------------------------------------------------

    [Fact]
    public void Player_StartsWithoutCitysBlessing()
    {
        _alice.HasCitysBlessing.Should().BeFalse(
            "a freshly-constructed player has no permanents and so has " +
            "never crossed the Ascend threshold (CR 702.131)");
    }

    [Fact]
    public void Player_WithFewerThanTenPermanents_DoesNotHaveCitysBlessing()
    {
        for (var i = 0; i < 9; i++) MakeDummyPermanent(_alice, $"Dummy {i}");

        _alice.EvaluateCitysBlessing();

        _alice.HasCitysBlessing.Should().BeFalse(
            "9 permanents is below the Ascend threshold of 10 (CR 702.131)");
    }

    [Fact]
    public void Player_AtExactlyTenPermanents_GainsCitysBlessing()
    {
        for (var i = 0; i < 10; i++) MakeDummyPermanent(_alice, $"Dummy {i}");

        _alice.EvaluateCitysBlessing();

        _alice.HasCitysBlessing.Should().BeTrue(
            "10 permanents meets the Ascend threshold (CR 702.131)");
    }

    [Fact]
    public void Player_TenthPermanentEntering_LatchesBlessing_AndFiresEvent()
    {
        var bus = new EventBus();
        _alice.AttachEventBus(bus);

        GainedCitysBlessingEvent? fired = null;
        bus.Subscribe<GainedCitysBlessingEvent>(e => fired = e);

        // Drop in 9 dummies first — no blessing yet.
        for (var i = 0; i < 9; i++)
        {
            var c = new Creature($"Dummy {i}", "{0}", 1, 1)
            {
                Owner = _alice,
                Controller = _alice,
            };
            c.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(c);
            bus.Publish(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield));
        }

        _alice.HasCitysBlessing.Should().BeFalse();
        fired.Should().BeNull();

        // The 10th entering permanent crosses the threshold.
        var tenth = new Creature("Tenth", "{0}", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
        };
        tenth.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(tenth);
        bus.Publish(new CardMovedEvent(tenth, ZoneType.Hand, ZoneType.Battlefield));

        _alice.HasCitysBlessing.Should().BeTrue(
            "the 10th permanent entering the battlefield latches the " +
            "city's blessing (CR 702.131)");
        fired.Should().NotBeNull(
            "the engine fires GainedCitysBlessingEvent on the latch transition");
        fired!.Player.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Player_PermanentLeaving_DoesNotRemoveCitysBlessing()
    {
        // CR 702.131c — once gained, the city's blessing lasts the rest of
        // the game even if the player's permanent count drops back below 10.
        var bus = new EventBus();
        _alice.AttachEventBus(bus);

        var perms = new List<Creature>();
        for (var i = 0; i < 10; i++)
        {
            var c = new Creature($"Dummy {i}", "{0}", 1, 1)
            {
                Owner = _alice,
                Controller = _alice,
            };
            c.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(c);
            bus.Publish(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield));
            perms.Add(c);
        }

        _alice.HasCitysBlessing.Should().BeTrue();

        // Sweep the board — the blessing stays.
        foreach (var c in perms)
        {
            _alice.Zones.Battlefield.RemoveCard(c);
            bus.Publish(new CardMovedEvent(c, ZoneType.Battlefield, ZoneType.Graveyard));
        }

        _alice.Zones.Battlefield.Count.Should().Be(0);
        _alice.HasCitysBlessing.Should().BeTrue(
            "CR 702.131c — once gained, the city's blessing is permanent " +
            "and never lost");
    }

    [Fact]
    public void Player_GainedCitysBlessingEvent_FiresOnlyOnce()
    {
        var bus = new EventBus();
        _alice.AttachEventBus(bus);

        var fireCount = 0;
        bus.Subscribe<GainedCitysBlessingEvent>(_ => fireCount++);

        // Bring count to 10 → fires once.
        for (var i = 0; i < 10; i++)
        {
            var c = new Creature($"Dummy {i}", "{0}", 1, 1)
            {
                Owner = _alice,
                Controller = _alice,
            };
            c.SetZone(ZoneType.Battlefield);
            _alice.Zones.Battlefield.AddCard(c);
            bus.Publish(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield));
        }

        // Another permanent enters — should NOT re-fire.
        var extra = new Creature("Extra", "{0}", 1, 1)
        {
            Owner = _alice,
            Controller = _alice,
        };
        extra.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(extra);
        bus.Publish(new CardMovedEvent(extra, ZoneType.Hand, ZoneType.Battlefield));

        fireCount.Should().Be(1,
            "GainedCitysBlessingEvent fires once on the latch transition; " +
            "subsequent ETBs do not re-fire");
    }

    [Fact]
    public void Player_OpponentsPermanents_DoNotCountTowardOwnBlessing()
    {
        // The Ascend threshold is per-player; permanents another player
        // controls don't push your count toward city's blessing.
        var bus = new EventBus();
        _alice.AttachEventBus(bus);
        _bob.AttachEventBus(bus);

        for (var i = 0; i < 10; i++)
        {
            var c = new Creature($"BobDummy {i}", "{0}", 1, 1)
            {
                Owner = _bob,
                Controller = _bob,
            };
            c.SetZone(ZoneType.Battlefield);
            _bob.Zones.Battlefield.AddCard(c);
            bus.Publish(new CardMovedEvent(c, ZoneType.Hand, ZoneType.Battlefield));
        }

        _bob.HasCitysBlessing.Should().BeTrue();
        _alice.HasCitysBlessing.Should().BeFalse(
            "the threshold counts permanents the asking player controls, " +
            "not opponents' (CR 702.131)");
    }

    // ------------------------------------------------------------------
    // Ocelot Pride retrofit — attack trigger doubles with the blessing.
    // ------------------------------------------------------------------

    [Fact]
    public void OcelotPride_Attack_WithoutCitysBlessing_CreatesOneCatToken()
    {
        var alice = new Player("Alice", 20);
        alice.HasCitysBlessing.Should().BeFalse();

        var ocelot = OcelotPrideFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ocelot);
        ocelot.SetZone(ZoneType.Battlefield);

        var attack = ocelot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CreatureAttacksEvent(ocelot, alice)));

        foreach (var effect in attack.Effects) effect.Execute();

        var cats = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Cat")
            .ToList();

        cats.Should().HaveCount(1,
            "without the city's blessing the attack trigger creates a " +
            "single 1/1 Cat token");
    }

    [Fact]
    public void OcelotPride_Attack_WithCitysBlessing_CreatesTwoCatTokens()
    {
        var alice = new Player("Alice", 20);
        for (var i = 0; i < 10; i++) MakeDummyPermanent(alice, $"Dummy {i}");
        alice.EvaluateCitysBlessing();
        alice.HasCitysBlessing.Should().BeTrue();

        var ocelot = OcelotPrideFactory.Create(alice);
        alice.Zones.Battlefield.AddCard(ocelot);
        ocelot.SetZone(ZoneType.Battlefield);

        var attack = ocelot.Abilities.OfType<TriggeredAbility>()
            .Single(t => t.IsTriggered(new CreatureAttacksEvent(ocelot, alice)));

        foreach (var effect in attack.Effects) effect.Execute();

        var cats = alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(c => c.IsToken && c.Name == "Cat")
            .ToList();

        cats.Should().HaveCount(2,
            "with the city's blessing the attack trigger creates two 1/1 " +
            "Cat tokens (CR 702.131 + CR 508.1f)");
    }
}
