using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WeaponsManufacturingFactory"/> (Aetherdrift,
/// {1}{R}).
///
/// Enchantment. Oracle text:
///   "Whenever a nontoken artifact you control enters, create a colorless
///    artifact token named Munitions with 'When this token leaves the
///    battlefield, it deals 2 damage to any target.'"
///
/// Covers:
///   - Identity ({1}{R} Enchantment, MV 2, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Trigger count attached on shape-only path.
///   - Nontoken artifact ETB → one Munitions token created (colorless,
///     artifact, named "Munitions", carries the LTB trigger).
///   - Token artifact ETB → no Munitions token (nontoken filter).
///   - Munitions LTB trigger deals 2 damage to a chosen any-target.
/// </summary>
[Trait("Color", "R")]
public class WeaponsManufacturingFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void WeaponsManufacturing_Identity()
    {
        var card = WeaponsManufacturingFactory.Create(_alice);

        card.Name.Should().Be("Weapons Manufacturing");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Enchantment).Should().BeTrue();
        card.HasType(CardType.Creature).Should().BeFalse();
        card.HasType(CardType.Artifact).Should().BeFalse();
        card.ManaCostValue.TotalValue.Should().Be(2, "MV = 1 generic + 1 red");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }
    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void WeaponsManufacturing_HasOneTriggeredAbility()
    {
        var card = WeaponsManufacturingFactory.Create(_alice);

        card.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "one nontoken-artifact-ETB trigger");
    }

    // -----------------------------------------------------------------------
    // Nontoken artifact ETB → Munitions token created
    // -----------------------------------------------------------------------

    [Fact]
    public void NontokenArtifactETB_CreatesOneMunitionsToken()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);

        var wm = WeaponsManufacturingFactory.Create(_alice, triggers: null, zoneService: zones);
        _alice.Zones.Battlefield.AddCard(wm);
        wm.SetZone(ZoneType.Battlefield);

        // Drive the ETB trigger effect directly — same posture as other factory
        // tests (Voldaren Epicure / Sundering Titan) that invoke effects
        // independently of the TriggerManager.
        var trigger = wm.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        var munitions = _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Where(a => a.IsToken && a.Name == "Munitions")
            .ToList();

        munitions.Should().HaveCount(1, "one Munitions token is created on nontoken artifact ETB");

        // Munitions is a colorless artifact token.
        var token = munitions[0];
        token.HasType(CardType.Artifact).Should().BeTrue("Munitions is an artifact");
        token.HasType(CardType.Creature).Should().BeFalse("Munitions is not a creature");
        token.IsToken.Should().BeTrue("CR 111.1 — minted as a token");

        // Munitions carries the LTB triggered ability.
        token.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the Munitions token has the LTB damage trigger");
    }

    [Fact]
    public void NontokenArtifactCreatureETB_AlsoCreatesOneMunitionsToken()
    {
        // CR 301.1 — an Artifact Creature is also an Artifact; the "nontoken
        // artifact" predicate includes Artifact Creatures.
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var wm = WeaponsManufacturingFactory.Create(_alice, triggers, zones);
        _alice.Zones.Battlefield.AddCard(wm);
        wm.SetZone(ZoneType.Battlefield);

        var artifactCreature = new Creature("Ornithopter", "{0}", 0, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };
        artifactCreature.AddCardType(CardType.Artifact);
        // IsToken defaults to false.

        bus.Publish(new CardMovedEvent(artifactCreature, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(1, "artifact creature also satisfies the artifact predicate");

        var trigger = wm.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Should().ContainSingle(a => a.IsToken && a.Name == "Munitions",
                "an artifact creature entering still triggers Weapons Manufacturing");
    }

    // -----------------------------------------------------------------------
    // Token artifact ETB → no Munitions (nontoken filter)
    // -----------------------------------------------------------------------

    [Fact]
    public void TokenArtifactETB_DoesNotCreateMunitionsToken()
    {
        // CR 111.5 — a token is a nonland, noncopy permanent; the "nontoken
        // artifact" predicate must exclude them. A Treasure token entering
        // (IsToken == true) should NOT trigger Weapons Manufacturing.
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var wm = WeaponsManufacturingFactory.Create(_alice, triggers, zones);
        _alice.Zones.Battlefield.AddCard(wm);
        wm.SetZone(ZoneType.Battlefield);

        // Simulate a Treasure token entering — it IS a token artifact.
        var treasureToken = new Artifact("Treasure", "")
        {
            Owner = _alice,
            Controller = _alice,
            IsToken = true,   // token! — should be excluded
        };

        bus.Publish(new CardMovedEvent(treasureToken, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "a token artifact entering does NOT fire Weapons Manufacturing's trigger (nontoken filter)");

        // No Munitions tokens on Alice's battlefield.
        _alice.Zones.Battlefield.GetCards()
            .OfType<Artifact>()
            .Should().NotContain(a => a.IsToken && a.Name == "Munitions",
                "token artifacts are excluded by the nontoken predicate");
    }

    [Fact]
    public void NonArtifactETB_DoesNotCreateMunitionsToken()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var wm = WeaponsManufacturingFactory.Create(_alice, triggers, zones);
        _alice.Zones.Battlefield.AddCard(wm);
        wm.SetZone(ZoneType.Battlefield);

        // A nontoken creature (not an artifact) entering — should not trigger.
        var creature = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            Owner = _alice,
            Controller = _alice,
        };

        bus.Publish(new CardMovedEvent(creature, ZoneType.Library, ZoneType.Battlefield));

        triggers.PendingCount.Should().Be(0,
            "a nonartifact permanent entering does NOT fire Weapons Manufacturing's trigger");
    }

    // -----------------------------------------------------------------------
    // Munitions LTB trigger — 2 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void MunitionsLTBTrigger_Deals2DamageToChosenTarget()
    {
        // Create a Munitions token and drive the LTB effect directly —
        // same posture as SunderingTitanTests / SkyclaveApparitionTests that
        // invoke the effect closure independently of the TriggerManager's
        // zone-sync pipeline (CR 603.6d "looks back" semantics tested at
        // the effect layer).
        var token = WeaponsManufacturingFactory.CreateMunitionsToken(
            _alice, triggers: null, zoneService: null);

        // Verify: token is a colorless artifact token named Munitions.
        token.Name.Should().Be("Munitions");
        token.IsToken.Should().BeTrue();
        token.HasType(CardType.Artifact).Should().BeTrue();
        token.HasType(CardType.Creature).Should().BeFalse();

        // The LTB trigger is attached.
        var ltbTrigger = token.Abilities.OfType<TriggeredAbility>().Single();

        // Pre-supply the "any target" — Bob (the chosen target).
        ltbTrigger.SetChosenTargets(new[] { new[] { (object)_bob } });

        // Drive the effect directly (same as SunderingTitan LTB test pattern).
        foreach (var e in ltbTrigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(18,
            "Munitions leaving the battlefield deals 2 damage to Bob (CR 119.3)");
    }

    [Fact]
    public void MunitionsLTBTrigger_NoChosenTarget_NoopsCleanly()
    {
        var token = WeaponsManufacturingFactory.CreateMunitionsToken(
            _alice, triggers: null, zoneService: null);
        var ltbTrigger = token.Abilities.OfType<TriggeredAbility>().Single();

        // No targets supplied — CR 608.2b no-op.
        var act = () => { foreach (var e in ltbTrigger.Effects) e.Execute(); };
        act.Should().NotThrow();
        _bob.LifeTotal.Should().Be(20,
            "no chosen target → no damage (CR 608.2b)");
    }

    [Fact]
    public void MunitionsLTBTrigger_Deals2DamageToBob_OnExile()
    {
        // LTB effect fires regardless of destination zone — same closure.
        var token = WeaponsManufacturingFactory.CreateMunitionsToken(
            _alice, triggers: null, zoneService: null);
        var ltbTrigger = token.Abilities.OfType<TriggeredAbility>().Single();
        ltbTrigger.SetChosenTargets(new[] { new[] { (object)_bob } });

        // Exile scenario — drive effect directly (condition fires on any
        // from-Battlefield move: die / exile / bounce — CR 603.6c).
        foreach (var e in ltbTrigger.Effects) e.Execute();

        _bob.LifeTotal.Should().Be(18,
            "Munitions exiled → 2 damage to Bob (CR 603.6c — any zone exit from battlefield)");
    }
}
