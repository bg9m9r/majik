using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="MindslaverFactory"/> — Legendary Artifact {6}
/// (Mirrodin):
///   "{4}, {T}, Sacrifice Mindslaver: You control target player during
///    that player's next turn."
///
/// Covers:
/// - Identity (Legendary Artifact, {6}, owner / controller).
/// - <see cref="NamedCardFactory"/> dispatch.
/// - Ability shape: one activated ability with {4} + tap + sacrifice and
///   a 1..1 target-player request.
/// - Resolution: Mindslaver self-sacs; the mind-control sink records the
///   chosen target.
/// - Resolution without a sink: Mindslaver self-sacs; no exception.
/// - Resolution with an illegal target (non-player): self-sac still runs;
///   sink is NOT invoked (CR 608.2b).
/// </summary>
public class MindslaverTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Mindslaver_IsLegendaryArtifact_AtCost6()
    {
        var slaver = MindslaverFactory.Create(_alice);

        slaver.Name.Should().Be("Mindslaver");
        slaver.ManaCost.Should().Be("{6}");
        slaver.HasType(CardType.Artifact).Should().BeTrue();
        slaver.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        slaver.Owner.Should().BeSameAs(_alice);
        slaver.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_Mindslaver()
    {
        var card = NamedCardFactory.Create("Mindslaver", _alice);

        card.Should().BeOfType<Artifact>();
        card.Name.Should().Be("Mindslaver");
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
    }

    [Fact]
    public void Mindslaver_AbilityShape_OneActivated_With4ManaTapSac_AndTargetPlayer()
    {
        var slaver = MindslaverFactory.Create(_alice);

        var ability = slaver.Abilities.OfType<ActivatedAbility>().Single();

        ability.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            c => c.Description.Contains("4"), "activation cost is {4}");
        ability.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Tap)
            .Should().Be(1);
        ability.Costs.OfType<AdditionalCost>().Count(c => c.CostType == AdditionalCostType.Sacrifice)
            .Should().Be(1);

        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.TargetRequests[0].Description.Should().Contain("player");
    }

    // -----------------------------------------------------------------------
    // Resolution
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_RecordsTargetViaSink_AndSacrificesMindslaver()
    {
        Player? sinkSeen = null;
        var slaver = MindslaverFactory.Create(_alice, mindControlSink: p => sinkSeen = p);
        _alice.Zones.Battlefield.AddCard(slaver);
        slaver.SetZone(ZoneType.Battlefield);

        var ability = slaver.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in ability.Effects) e.Execute();

        sinkSeen.Should().BeSameAs(_bob,
            "the mind-control sink records the chosen target player");

        slaver.Zone.Should().Be(ZoneType.Graveyard,
            "sacrifice cost moves Mindslaver to its owner's graveyard (CR 701.16)");
        _alice.Zones.Graveyard.GetCards().Should().Contain(slaver);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(slaver);
    }

    [Fact]
    public void Activate_WithoutSink_StillSacrificesMindslaver()
    {
        var slaver = MindslaverFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(slaver);
        slaver.SetZone(ZoneType.Battlefield);

        var ability = slaver.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { _bob },
        });

        foreach (var e in ability.Effects) e.Execute();

        slaver.Zone.Should().Be(ZoneType.Graveyard);
        _alice.Zones.Graveyard.GetCards().Should().Contain(slaver);
    }

    [Fact]
    public void Activate_IllegalTarget_NotInvokedButStillSacrificed()
    {
        // CR 608.2b — illegal target → mind-control effect does nothing.
        // The sacrifice cost was still paid.
        Player? sinkSeen = null;
        var slaver = MindslaverFactory.Create(_alice, mindControlSink: p => sinkSeen = p);
        _alice.Zones.Battlefield.AddCard(slaver);
        slaver.SetZone(ZoneType.Battlefield);

        var notAPlayer = new Artifact("Some Artifact", "{0}");
        var ability = slaver.Abilities.OfType<ActivatedAbility>().Single();
        ability.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { notAPlayer },
        });

        foreach (var e in ability.Effects) e.Execute();

        sinkSeen.Should().BeNull("illegal target → sink not invoked (CR 608.2b)");
        slaver.Zone.Should().Be(ZoneType.Graveyard,
            "the cost was paid before the resolution-time legality check");
    }

    // -----------------------------------------------------------------------
    // CR 720 — live control grant via the ControlPlayerRegistry
    // -----------------------------------------------------------------------

    [Fact]
    public void Activate_WithLiveRegistry_GrantsControlOfTargetsNextTurn()
    {
        // CR 720.1 — "You control target player during that player's next
        // turn." Register a live ControlPlayerRegistry for the controller so
        // the effect's provider lookup resolves it, then resolve Mindslaver
        // and assert the grant landed.
        var registry = new ControlPlayerRegistry();
        ControlPlayerRegistryProvider.Set(_alice, registry);
        try
        {
            var slaver = MindslaverFactory.Create(_alice);
            _alice.Zones.Battlefield.AddCard(slaver);
            slaver.SetZone(ZoneType.Battlefield);

            var ability = slaver.Abilities.OfType<ActivatedAbility>().Single();
            ability.SetChosenTargets(new IReadOnlyList<object>[]
            {
                new object[] { _bob },
            });

            registry.HasPendingControl(_bob).Should().BeFalse("no grant before resolution");

            foreach (var e in ability.Effects) e.Execute();

            // CR 720.1 — Bob's next turn is now under Alice's control.
            registry.HasPendingControl(_bob).Should().BeTrue();
            registry.ConsumeControlFor(_bob, out var controller).Should().BeTrue();
            controller.Should().BeSameAs(_alice);

            // CR 701.16 — Mindslaver was sacrificed as part of the cost.
            slaver.Zone.Should().Be(ZoneType.Graveyard);
        }
        finally
        {
            ControlPlayerRegistryProvider.Remove(_alice);
        }
    }
}
