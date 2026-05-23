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

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="ValakutTheMoltenPinnacleFactory"/>.
///
/// Covers:
///   - Card identity (name, type Land, owner / controller, exactly one
///     ManaAbility + one TriggeredAbility, no Mountain subtype).
///   - <see cref="NamedCardFactory"/> dispatch hands back the same shape.
///   - Conditional ETB-tapped (CR 614.1c): &lt;5 Mountains controlled → enters
///     tapped; ≥5 Mountains controlled → enters untapped.
///   - Mountain ETB while controller has ≥5 other Mountains → trigger
///     queued (one CardMovedEvent → exactly one pending trigger).
///   - Mountain ETB while controller has &lt;5 other Mountains → trigger
///     does NOT fire (intervening-if false).
/// </summary>
public class ValakutTheMoltenPinnacleTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Valakut_Identity_LandWithTapMana_AndMountainTrigger()
    {
        var v = ValakutTheMoltenPinnacleFactory.Create(_alice);

        v.Name.Should().Be("Valakut, the Molten Pinnacle");
        v.HasType(CardType.Land).Should().BeTrue();
        v.HasSubtype(CardSubtype.Mountain).Should().BeFalse(
            "Valakut is a non-basic Land with no printed Mountain subtype");
        v.HasSupertype(CardSupertype.Basic).Should().BeFalse();
        v.Owner.Should().BeSameAs(_alice);
        v.Controller.Should().BeSameAs(_alice);

        // {T}: Add {R} + the Mountain-ETB intervening-if trigger.
        v.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        v.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void Valakut_NamedCardFactory_DispatchesShape()
    {
        var card = NamedCardFactory.Create("Valakut, the Molten Pinnacle", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Valakut, the Molten Pinnacle");
        card.HasType(CardType.Land).Should().BeTrue();
        card.HasSubtype(CardSubtype.Mountain).Should().BeFalse();
        card.Abilities.OfType<ManaAbility>().Should().HaveCount(1);
        card.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Conditional ETB-tapped (CR 614.1c)
    // -----------------------------------------------------------------------

    [Fact]
    public void EtbWithFewerThanFiveMountains_EntersTapped()
    {
        var (zones, _, _, replacements) = BuildEngine();

        // Seed 4 Mountains on Alice's battlefield (one shy of the threshold).
        for (int i = 0; i < 4; i++)
        {
            var m = new Land($"Mountain {i}", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
            m.SetOwner(_alice);
            m.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(m);
            m.SetZone(ZoneType.Battlefield);
        }

        // Build Valakut wired to the live ReplacementBus.
        var valakut = ValakutTheMoltenPinnacleFactory.Create(_alice, replacements, triggers: null);
        _alice.Zones.Hand.AddCard(valakut);
        valakut.SetZone(ZoneType.Hand);

        zones.MoveCardTo(valakut, ZoneType.Battlefield, controller: _alice);

        valakut.Zone.Should().Be(ZoneType.Battlefield);
        valakut.IsTapped.Should().BeTrue(
            "with only 4 other Mountains controlled, the ETB-tapped replacement fires");
    }

    [Fact]
    public void EtbWithFiveOrMoreMountains_EntersUntapped()
    {
        var (zones, _, _, replacements) = BuildEngine();

        // Seed exactly 5 Mountains on Alice's battlefield.
        for (int i = 0; i < 5; i++)
        {
            var m = new Land($"Mountain {i}", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
            m.SetOwner(_alice);
            m.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(m);
            m.SetZone(ZoneType.Battlefield);
        }

        var valakut = ValakutTheMoltenPinnacleFactory.Create(_alice, replacements, triggers: null);
        _alice.Zones.Hand.AddCard(valakut);
        valakut.SetZone(ZoneType.Hand);

        zones.MoveCardTo(valakut, ZoneType.Battlefield, controller: _alice);

        valakut.Zone.Should().Be(ZoneType.Battlefield);
        valakut.IsTapped.Should().BeFalse(
            "with ≥5 other Mountains controlled, Valakut sidesteps the ETB-tapped clause");
    }

    // -----------------------------------------------------------------------
    // Triggered ability (CR 603.1 / 603.4 — intervening-if)
    // -----------------------------------------------------------------------

    [Fact]
    public void MountainEtbWithFiveOtherMountains_TriggerFires()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // Plant 5 Mountains BEFORE Valakut so the intervening-if sees ≥5
        // OTHER Mountains when the next Mountain enters.
        for (int i = 0; i < 5; i++)
        {
            var m = new Land($"Mountain {i}", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
            m.SetOwner(_alice);
            m.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(m);
            m.SetZone(ZoneType.Battlefield);
        }

        var valakut = ValakutTheMoltenPinnacleFactory.Create(_alice, replacements, triggers);
        valakut.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(valakut);
        valakut.SetZone(ZoneType.Battlefield);

        // Play a fresh Mountain via ZoneService — its CardMovedEvent
        // drives Valakut's trigger.
        var trigger = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        trigger.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(trigger);
        trigger.SetZone(ZoneType.Hand);

        zones.MoveCardTo(trigger, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(1,
            "the entering Mountain is the 6th; intervening-if (≥5 OTHER Mountains, excluding itself) is satisfied");
    }

    [Fact]
    public void MountainEtbWithFewerThanFiveOtherMountains_TriggerDoesNotFire()
    {
        var (zones, _, triggers, replacements) = BuildEngine();

        // Plant only 4 Mountains — the next Mountain ETB sees 4 OTHER
        // Mountains (intervening-if fails).
        for (int i = 0; i < 4; i++)
        {
            var m = new Land($"Mountain {i}", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
            m.SetOwner(_alice);
            m.SetController(_alice);
            _alice.Zones.Battlefield.AddCard(m);
            m.SetZone(ZoneType.Battlefield);
        }

        var valakut = ValakutTheMoltenPinnacleFactory.Create(_alice, replacements, triggers);
        valakut.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(valakut);
        valakut.SetZone(ZoneType.Battlefield);

        var trigger = new Land("Mountain", new[] { CardSupertype.Basic }, new[] { CardSubtype.Mountain });
        trigger.SetOwner(_alice);
        _alice.Zones.Hand.AddCard(trigger);
        trigger.SetZone(ZoneType.Hand);

        zones.MoveCardTo(trigger, ZoneType.Battlefield, controller: _alice);

        triggers.PendingCount.Should().Be(0,
            "only 4 other Mountains after the ETB; intervening-if fails");
    }

    // -----------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------

    private static (ZoneService zones, Majik.Core.Stack.Stack stack, TriggerManager triggers, ReplacementBus replacements) BuildEngine()
    {
        var bus = new EventBus();
        var rep = new ReplacementBus();
        var zones = new ZoneService(bus, rep);
        var stack = new Majik.Core.Stack.Stack(bus);
        var triggers = new TriggerManager(stack, bus);
        return (zones, stack, triggers, rep);
    }
}
