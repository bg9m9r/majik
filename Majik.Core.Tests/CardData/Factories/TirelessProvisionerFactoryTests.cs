using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Effects;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="TirelessProvisionerFactory"/>.
///
/// Covers:
/// - Identity (Elf Scout 3/2 at {2}{G}; owner / controller wired; exactly
///   one TriggeredAbility).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - End-to-end landfall trigger via bus + stack: a land entering under the
///   controller fires the trigger and resolves into a Treasure token by
///   default (no agent registered → deterministic Treasure pick).
/// - Trigger does NOT fire when an opponent controls the entering land
///   ("under YOUR control").
/// - Trigger does NOT fire for non-land ETB (oracle: "a land enters").
/// </summary>
[Trait("Color", "G")]
public class TirelessProvisionerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    [Fact]
    public void TirelessProvisioner_Identity()
    {
        var c = TirelessProvisionerFactory.Create(_alice);

        c.Name.Should().Be("Tireless Provisioner");
        c.ManaCost.Should().Be("{2}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Scout).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }
    [Fact]
    public void LandEntersUnderController_DefaultsToTreasureToken()
    {
        var (zones, stack, triggers) = BuildEngine();

        var provisioner = TirelessProvisionerFactory.Create(_alice, zones, triggers);
        provisioner.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(provisioner);

        var forest = new Land("Forest",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(forest);
        forest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(forest, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "exactly one landfall trigger should be queued");

        triggers.PutPendingTriggersOnStack(_alice);
        stack.Pop()!.Resolve();

        // No agent registered → Treasure default.
        var treasures = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.HasSubtype(CardSubtype.Treasure))
            .ToList();
        treasures.Should().HaveCount(1,
            "default mode pick (no agent) creates a Treasure token");
        treasures[0].IsToken.Should().BeTrue();

        // No Food tokens in the absence of an agent.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Any(a => a.HasSubtype(CardSubtype.Food))
            .Should().BeFalse();
    }

    [Fact]
    public void LandEntersUnderOpponent_NoTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var provisioner = TirelessProvisionerFactory.Create(_alice, zones, triggers);
        provisioner.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(provisioner);

        // Bob plays a land — Alice's Provisioner must NOT trigger.
        var bobForest = new Land("Forest",
            new[] { CardSupertype.Basic }, new[] { CardSubtype.Forest });
        bobForest.SetOwner(_bob);
        _bob.Zones.Hand.AddCard(bobForest);
        bobForest.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bobForest, ZoneType.Battlefield, controller: _bob);

        triggers.PendingCount.Should().Be(0,
            "opponent's land does not satisfy 'under your control'");
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Any(a => a.HasSubtype(CardSubtype.Treasure) || a.HasSubtype(CardSubtype.Food))
            .Should().BeFalse();
    }

    [Fact]
    public void NonLandEnters_NoTrigger()
    {
        var (zones, _, triggers) = BuildEngine();

        var provisioner = TirelessProvisionerFactory.Create(_alice, zones, triggers);
        provisioner.SetZone(ZoneType.Battlefield);
        _alice.Zones.Battlefield.AddCard(provisioner);

        // A creature ETB does not satisfy the "a land enters" predicate.
        var bear = new Creature("Grizzly Bears", "1G", 2, 2);
        bear.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(bear);
        bear.SetZone(ZoneType.Hand);

        zones.MoveCardTo(bear, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "trigger gates on HasType(Land); a creature ETB doesn't match");
    }

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers);
    }
}
