using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="NestInvaderFactory"/> (Rise of the Eldrazi,
/// {1}{G}).
///
/// Creature — Eldrazi Drone 2/2 (green). Oracle text (verified against
/// Scryfall):
///   "When this creature enters, create a 0/1 colorless Eldrazi Spawn
///    creature token. It has \"Sacrifice this token: Add {C}.\""
///
/// Covers:
///   - Identity (Eldrazi Drone 2/2 at {1}{G}, green, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - One ETB trigger attached structurally on the shape-only path.
///   - ETB trigger mints a 0/1 colorless Eldrazi Spawn token with a
///     sac-for-{C} mana ability under the controller.
/// </summary>
[Trait("Color", "G")]
public class NestInvaderFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void NestInvader_Identity()
    {
        var c = NestInvaderFactory.Create(_alice);

        c.Name.Should().Be("Nest Invader");
        c.ManaCost.Should().Be("{1}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        c.HasSubtype(CardSubtype.Drone).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(2);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        // CR 105.2 — {1}{G} carries a green pip, so Nest Invader is green.
        CardColors.GetColors(c).Should().ContainSingle()
            .Which.Should().Be(ManaColor.Green);
    }

    [Fact]
    public void NestInvader_DispatchesViaNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Nest Invader", _alice);

        card.Should().BeOfType<Creature>("Nest Invader is a Creature instance");
        card.Name.Should().Be("Nest Invader");
        card.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        card.HasSubtype(CardSubtype.Drone).Should().BeTrue();
    }

    [Fact]
    public void NestInvader_HasOneEtbTrigger()
    {
        var c = NestInvaderFactory.Create(_alice);

        var triggers = c.Abilities.OfType<TriggeredAbility>().ToList();
        triggers.Should().HaveCount(1, "the single printed ETB Spawn-token trigger");
        triggers[0].Condition.EventType.Should().Be(typeof(CardMovedEvent),
            "the ETB trigger watches battlefield-entry CardMovedEvent");
    }

    [Fact]
    public void EtbTrigger_MintsZeroOneColourlessEldraziSpawn_UnderController()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = NestInvaderFactory.Create(_alice, zones, triggers);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        // A single 0/1 colorless Eldrazi Spawn token on Alice's battlefield.
        var spawns = _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Where(t => t.IsToken && t.HasSubtype(CardSubtype.Spawn))
            .ToList();
        spawns.Should().HaveCount(1, "the ETB effect mints one Eldrazi Spawn");

        var spawn = spawns[0];
        spawn.Name.Should().Be("Eldrazi Spawn");
        spawn.HasSubtype(CardSubtype.Eldrazi).Should().BeTrue();
        spawn.BasePower.Should().Be(0);
        spawn.BaseToughness.Should().Be(1);
        CardColors.GetColors(spawn).Should().BeEmpty("Eldrazi Spawn tokens are colorless");

        // "Sacrifice this token: Add {C}." — wired as a ManaAbility (sac
        // cost deferred, same posture as Treasure/Food, see TokenFactory).
        spawn.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "the Spawn carries the Add {C} mana ability");
    }

    [Fact]
    public void EtbTrigger_ShapeOnlyPath_MintsSpawnWithRawZoneMove()
    {
        // Shape-only Create(owner) — no ZoneService. The token half still
        // mints via the raw zone-move fallback in TokenFactory.
        var card = NestInvaderFactory.Create(_alice);

        var etb = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in etb.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Creature>()
            .Count(t => t.IsToken && t.HasSubtype(CardSubtype.Spawn))
            .Should().Be(1, "the ETB effect mints one Eldrazi Spawn even without a ZoneService");
    }
}
