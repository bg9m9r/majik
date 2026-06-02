using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Events;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="LanternOfInsightFactory"/> — Artifact {1}:
///   "Players play with the top card of their libraries revealed.
///    {T}, Sacrifice this artifact: Target player shuffles."
///
/// Covers:
/// - Card identity (Artifact, {1}, owner / controller).
/// - NamedCardFactory dispatch.
/// - Ability shape: one StaticAbility (top-revealed rider) + one
///   ActivatedAbility ({T}, Sacrifice: target player shuffles).
/// - Activated ability cost shape (tap + sacrifice) and a single player target.
/// - Activated ability resolution: sacrifices the artifact and shuffles the
///   target player's library (CR 701.20 — asserted via LibraryShuffledEvent).
/// - Resolution falls back to the controller when no target is set.
/// - LookAtTopOfLibrary peek helper.
/// </summary>
public class LanternOfInsightFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Card identity
    // -----------------------------------------------------------------------

    [Fact]
    public void LanternOfInsight_IsArtifact_WithGenericManaCost()
    {
        var lantern = LanternOfInsightFactory.Create(_alice);

        lantern.HasType(CardType.Artifact).Should().BeTrue();
        lantern.Name.Should().Be("Lantern of Insight");
        lantern.ManaCost.Should().Be("{1}");
        lantern.Owner.Should().BeSameAs(_alice);
        lantern.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_LanternOfInsight()
    {
        var card = NamedCardFactory.Create("Lantern of Insight", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Lantern of Insight");
        card.HasType(CardType.Artifact).Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Ability shape
    // -----------------------------------------------------------------------

    [Fact]
    public void LanternOfInsight_HasTopRevealedStatic_AndOneActivatedAbility()
    {
        var lantern = LanternOfInsightFactory.Create(_alice);

        lantern.Abilities.OfType<StaticAbility>()
            .Should().Contain(s => s.Description == LanternOfInsightFactory.TopRevealedDescription,
                "CR 604.1 — \"Players play with the top card of their libraries revealed.\"");
        lantern.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    [Fact]
    public void ActivatedAbility_HasTapAndSacrificeCosts_AndOnePlayerTarget()
    {
        var lantern = LanternOfInsightFactory.Create(_alice);

        var ability = lantern.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Tap,
                "the shuffle ability costs {T}");
        ability.Costs.OfType<AdditionalCost>()
            .Should().Contain(c => c.CostType == AdditionalCostType.Sacrifice,
                "the shuffle ability sacrifices the artifact");

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("player");
    }

    // -----------------------------------------------------------------------
    // Activated ability resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void ActivatedAbility_ShufflesTargetPlayerLibrary_AndSacrificesLantern()
    {
        // Register an event bus for Bob so the shuffle publishes a
        // LibraryShuffledEvent we can assert on.
        var bus = new EventBus();
        var shuffles = new List<LibraryShuffledEvent>();
        bus.Subscribe<LibraryShuffledEvent>(shuffles.Add);
        EventBusRegistry.Set(_bob, bus);
        try
        {
            // Give Bob a couple of cards to shuffle.
            var c1 = new Card("Lib 1", "{1}");
            var c2 = new Card("Lib 2", "{2}");
            _bob.Zones.Library.AddCard(c1);
            c1.SetZone(ZoneType.Library);
            _bob.Zones.Library.AddCard(c2);
            c2.SetZone(ZoneType.Library);

            var lantern = LanternOfInsightFactory.Create(_alice);
            _alice.Zones.Battlefield.AddCard(lantern);
            lantern.SetZone(ZoneType.Battlefield);

            var ability = lantern.Abilities.OfType<ActivatedAbility>().Single();
            ability.SetChosenTargets(new IReadOnlyList<object>[]
            {
                new object[] { _bob },
            });

            ability.Resolve();

            // Bob's library was shuffled once (cards remain, just reordered).
            shuffles.Should().HaveCount(1, "the target player shuffles their library (CR 701.20)");
            shuffles[0].Player.Should().BeSameAs(_bob);
            shuffles[0].Reason.Should().Be(LanternOfInsightFactory.Slug);
            _bob.Zones.Library.GetCards().Should().HaveCount(2);

            // Lantern has been sacrificed (Battlefield → owner's graveyard).
            _alice.Zones.Graveyard.GetCards().Should().Contain(lantern);
            _alice.Zones.Battlefield.GetCards().Should().NotContain(lantern);
            lantern.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            EventBusRegistry.Remove(_bob);
        }
    }

    [Fact]
    public void ActivatedAbility_NoTargetSet_FallsBackToController_AndSacrificesLantern()
    {
        var bus = new EventBus();
        var shuffles = new List<LibraryShuffledEvent>();
        bus.Subscribe<LibraryShuffledEvent>(shuffles.Add);
        EventBusRegistry.Set(_alice, bus);
        try
        {
            var lantern = LanternOfInsightFactory.Create(_alice);
            _alice.Zones.Battlefield.AddCard(lantern);
            lantern.SetZone(ZoneType.Battlefield);

            var ability = lantern.Abilities.OfType<ActivatedAbility>().Single();
            // No SetChosenTargets call — v1 falls back to the controller.

            ability.Resolve();

            // Controller (Alice) shuffled.
            shuffles.Should().HaveCount(1);
            shuffles[0].Player.Should().BeSameAs(_alice);

            // Lantern sacrificed regardless.
            _alice.Zones.Graveyard.GetCards().Should().Contain(lantern);
            lantern.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            EventBusRegistry.Remove(_alice);
        }
    }

    // -----------------------------------------------------------------------
    // Top-of-library peek helper
    // -----------------------------------------------------------------------

    [Fact]
    public void LookAtTopOfLibrary_ReturnsTopCard_OrNullWhenEmpty()
    {
        LanternOfInsightFactory.LookAtTopOfLibrary(_alice).Should().BeNull(
            "an empty library reveals nothing");

        var top = new Card("Top", "{1}");
        _alice.Zones.Library.AddCard(top);
        top.SetZone(ZoneType.Library);

        LanternOfInsightFactory.LookAtTopOfLibrary(_alice).Should().BeSameAs(top);
    }
}
