using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Services;
using Majik.Core.StateMachine;
using Majik.Core.ValueObjects;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SparkElementalFactory"/> (Tenth Edition, {R}).
///
/// Creature — Elemental 3/1. Oracle text (verified against Scryfall):
///   "Trample, haste
///    At the beginning of the end step, sacrifice this creature."
///
/// Covers:
///   - Identity (Elemental 3/1 at {R}, red, owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch (JSON-backed base shape).
///   - Trample + Haste keyword markers (CR 702.19 / CR 702.10).
///   - One end-step triggered ability attached structurally (shape-only path).
///   - Resolve: the Elemental is sacrificed (battlefield → graveyard).
///   - End-step trigger is unscoped — fires on any player's end step
///     (CR 603.3d).
/// </summary>
[Trait("Color", "R")]
public class SparkElementalFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // ── Identity / dispatch ─────────────────────────────────────────────

    [Fact]
    public void SparkElemental_Identity()
    {
        var c = SparkElementalFactory.Create(_alice);

        c.Name.Should().Be("Spark Elemental");
        c.ManaCost.Should().Be("{R}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elemental).Should().BeTrue();
        c.BasePower.Should().Be(3);
        c.BaseToughness.Should().Be(1);
        CardColors.GetColors(c).Should().Contain(ManaColor.Red);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    // ── Keywords ────────────────────────────────────────────────────────

    [Fact]
    public void SparkElemental_HasTrampleAndHaste()
    {
        var c = SparkElementalFactory.Create(_alice);

        CombatAbilities.HasTrample(c).Should().BeTrue("CR 702.19 — Trample");
        CombatAbilities.HasHaste(c).Should().BeTrue("CR 702.10 — haste");
    }

    // ── End-step self-sacrifice ─────────────────────────────────────────

    [Fact]
    public void SparkElemental_HasOneEndStepTriggeredAbility()
    {
        var c = SparkElementalFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().HaveCount(1, "the printed end-step self-sacrifice trigger");
    }

    [Fact]
    public void EndStep_Resolve_SacrificesThisCreature()
    {
        var bus = new EventBus();
        var zones = new ZoneService(bus);
        var triggers = new TriggerManager(new Majik.Core.Stack.Stack(), bus);

        var card = SparkElementalFactory.Create(_alice, triggers, zones);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        // "At the beginning of the end step, sacrifice this creature."
        // CR 701.16 — battlefield → owner's graveyard.
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(card);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }

    [Fact]
    public void EndStep_TriggerFiresOnAnyPlayersEndStep()
    {
        // CR 603.3d — an unscoped "At the beginning of the end step" trigger
        // fires on every player's end step, not just the controller's.
        var card = SparkElementalFactory.Create(_alice);
        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();

        var aliceEnd = new StepStartedEvent(StepStateType.End, _alice);
        var bobEnd = new StepStartedEvent(StepStateType.End, _bob);
        var bobUpkeep = new StepStartedEvent(StepStateType.Upkeep, _bob);

        trigger.Condition.Matches(aliceEnd, null!).Should().BeTrue();
        trigger.Condition.Matches(bobEnd, null!).Should().BeTrue();
        trigger.Condition.Matches(bobUpkeep, null!).Should().BeFalse();
    }

    [Fact]
    public void EndStep_WithoutZoneService_StillSacrifices()
    {
        var card = SparkElementalFactory.Create(_alice);

        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        card.Zone.Should().Be(ZoneType.Graveyard);
    }

    [Fact]
    public void SelfSac_OnProdPath_PublishesPermanentSacrificedEvent()
    {
        // class-(b) sac-bus pay-down: the routed prod overload threads
        // effects.EventBus into the end-step self-sac closure so the
        // CR 701.16 sacrifice publishes a PermanentSacrificedEvent (CR
        // 701.16a). Drives the card EXACTLY as prod does.
        var bus = new EventBus();
        var effects = new Majik.Core.Effects.ContinuousEffectsService(bus);

        var captured = new System.Collections.Generic.List<PermanentSacrificedEvent>();
        bus.Subscribe<PermanentSacrificedEvent>(captured.Add);

        var built = NamedCardFactory.Create("Spark Elemental", _alice, effects);
        built.Should().BeOfType<Creature>();
        var card = (Creature)built;
        _alice.Zones.Battlefield.AddCard(card);
        card.SetZone(ZoneType.Battlefield);

        var trigger = card.Abilities.OfType<TriggeredAbility>().Single();
        foreach (var e in trigger.Effects) e.Execute();

        captured.Should().ContainSingle(
            "the prod effects-aware dispatch threads the bus so the self-sacrifice "
            + "publishes PermanentSacrificedEvent (CR 701.16a)")
            .Which.SacrificingPlayer.Should().BeSameAs(_alice);
        card.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(card);
    }
}
