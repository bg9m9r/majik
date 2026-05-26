using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Effects;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="CauldronOfSoulsFactory"/> (Shadowmoor, {4}).
///
/// Covers:
/// - Identity (Artifact, {4}, owner/controller).
/// - NamedCardFactory dispatch.
/// - Activated ability shape — tap cost + any-number-of-target-creatures
///   request (MinTargets = 0, MaxTargets = int.MaxValue).
/// - Resolution grants the configured Persist-approximation keyword to
///   each chosen creature via its ActiveEffects service.
/// - Resolution silently no-ops on creatures off the battlefield
///   (CR 608.2b illegal-target filter) and on creatures without an
///   ActiveEffects service wired (shape-only path).
/// - Empty target set is a clean no-op (CR 601.2c "any number of"
///   includes zero).
/// </summary>
public class CauldronOfSoulsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void CauldronOfSouls_Identity()
    {
        var c = CauldronOfSoulsFactory.Create(_alice);

        c.Name.Should().Be("Cauldron of Souls");
        c.ManaCost.Should().Be("{4}");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);

        var activated = c.Abilities.OfType<ActivatedAbility>().ToList();
        activated.Should().HaveCount(1);

        // Costs: single tap cost (CR 602.1b).
        activated[0].Costs.Should().ContainSingle()
            .Which.Should().BeOfType<AdditionalCost>();

        // Target request: any-number-of-target-creatures (CR 601.2c).
        activated[0].TargetRequests.Should().HaveCount(1);
        activated[0].TargetRequests[0].MinTargets.Should().Be(0,
            "CR 601.2c — 'any number of' includes zero targets");
        activated[0].TargetRequests[0].MaxTargets.Should().Be(int.MaxValue);
    }

    [Fact]
    public void CauldronOfSouls_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Cauldron of Souls", _alice);

        c.Should().BeOfType<Artifact>();
        c.Name.Should().Be("Cauldron of Souls");
    }

    [Fact]
    public void CauldronOfSouls_GrantsKeywordToChosenCreatures()
    {
        var cauldron = CauldronOfSoulsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cauldron);
        cauldron.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var giant = new Creature("Hill Giant", "{3}{R}", 3, 3)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        giant.SetOwner(_alice);
        giant.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(giant);
        giant.SetZone(ZoneType.Battlefield);

        var activated = cauldron.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear, giant },
        });

        foreach (var e in activated.Effects) e.Execute();

        // The keyword string is registered on each creature's
        // ActiveEffects service. Verify the Layer-6 grant propagated to
        // the creature's computed keyword set.
        bear.ActiveEffects!.Compute(bear).Keywords
            .Should().Contain(CauldronOfSoulsFactory.GrantedKeyword,
                "Cauldron grants the Persist-approximation keyword (Indestructible v1)");
        giant.ActiveEffects!.Compute(giant).Keywords
            .Should().Contain(CauldronOfSoulsFactory.GrantedKeyword);
    }

    [Fact]
    public void CauldronOfSouls_EmptyTargetSet_NoOp()
    {
        // CR 601.2c — "any number of" includes zero. An activation that
        // picks no targets must resolve cleanly without throwing.
        var cauldron = CauldronOfSoulsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cauldron);
        cauldron.SetZone(ZoneType.Battlefield);

        var activated = cauldron.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            Array.Empty<object>(),
        });

        var act = () =>
        {
            foreach (var e in activated.Effects) e.Execute();
        };
        act.Should().NotThrow();
    }

    [Fact]
    public void CauldronOfSouls_IllegalTargetOffBattlefield_SkipsCreature()
    {
        // CR 608.2b — if a chosen target is no longer on the battlefield
        // at resolution, it becomes illegal and is skipped. Verify the
        // resolution closure honours this by NOT registering the grant
        // on an off-battlefield creature.
        var cauldron = CauldronOfSoulsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(cauldron);
        cauldron.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2)
        {
            ActiveEffects = new ContinuousEffectsService(),
        };
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        // Place it on graveyard, not battlefield.
        _alice.Zones.Graveyard.AddCard(bear);
        bear.SetZone(ZoneType.Graveyard);

        var activated = cauldron.Abilities.OfType<ActivatedAbility>().Single();
        activated.SetChosenTargets(new IReadOnlyList<object>[]
        {
            new object[] { bear },
        });

        foreach (var e in activated.Effects) e.Execute();

        bear.ActiveEffects!.Compute(bear).Keywords
            .Should().NotContain(CauldronOfSoulsFactory.GrantedKeyword,
                "CR 608.2b — off-battlefield target is illegal at resolution and skipped");
    }
}
