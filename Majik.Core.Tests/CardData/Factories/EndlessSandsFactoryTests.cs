using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Players;
using Majik.Core.Players.Agents;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for <see cref="EndlessSandsFactory"/> (Hour of Devastation). Land —
/// Desert:
///   "{T}: Add {C}.
///    {2}, {T}: Exile target creature you control.
///    {4}, {T}, Sacrifice this land: Return each creature card exiled with
///    this land to the battlefield under its owner's control."
///
/// A colourless {C}-producing Desert (the {T}: Add {C} base is shared with
/// <see cref="HostileDesertFactory"/>). The blink half mirrors the
/// "exiled-with-this-source ledger + return" pattern of
/// <see cref="BomatCourierFactory"/>: the second ability records the exiled
/// creature in a per-land ledger; the third ability returns every card still
/// in that ledger (and still in exile) to the battlefield under its OWNER's
/// control.
///
/// Covers:
/// - Identity (Land + Desert subtype, no supertype, name, owner/controller).
/// - JSON-backed {T}: Add {C} mana ability (one).
/// - Two activated abilities with the right cost shapes
///   ({2},{T} target-creature exile; {4},{T},Sac return).
/// - The exile ability records the creature in the ledger and moves it to
///   exile.
/// - The return ability brings each ledgered creature back to the
///   battlefield under its owner's control, and drains the ledger.
/// </summary>
[Trait("Color", "C")]
public class EndlessSandsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void EndlessSands_Identity()
    {
        var land = EndlessSandsFactory.Create(_alice);

        land.Name.Should().Be("Endless Sands");
        land.HasType(CardType.Land).Should().BeTrue();
        land.HasSubtype(CardSubtype.Desert).Should().BeTrue(
            "printed type line is \"Land — Desert\"");
        land.HasType(CardType.Creature).Should().BeFalse(
            "printed shape is a plain land");
        land.HasSupertype(CardSupertype.Basic).Should().BeFalse(
            "Endless Sands is a nonbasic land");
        land.Owner.Should().BeSameAs(_alice);
        land.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void EndlessSands_DispatchesThroughNamedCardFactory()
    {
        var card = NamedCardFactory.Create("Endless Sands", _alice);

        card.Should().BeOfType<Land>();
        card.Name.Should().Be("Endless Sands");
    }

    [Fact]
    public void EndlessSands_HasColorlessManaAndTwoActivatedAbilities()
    {
        var land = EndlessSandsFactory.Create(_alice);

        land.Abilities.OfType<ManaAbility>().Should().HaveCount(1,
            "{T}: Add {C} is wired from the JSON definition");
        land.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2,
            "the {2},{T} exile ability and the {4},{T},Sac return ability");
        land.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Endless Sands has no triggered ability");
    }

    // -----------------------------------------------------------------------
    // Ability cost shapes
    // -----------------------------------------------------------------------

    [Fact]
    public void ExileAbility_HasGenericTwoPlusTapCost_AndTargetsACreature()
    {
        var land = EndlessSandsFactory.Create(_alice);

        var exile = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("2")));

        exile.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the {2} generic mana component");
        exile.Costs.OfType<AdditionalCost>().Should().Contain(
            c => c.Description.Contains("Tap"),
            "the {T} component is wired");
        exile.TargetRequests.Should().ContainSingle(
            "\"target creature you control\" is one target request");
        exile.TargetRequests[0].MinTargets.Should().Be(1);
        exile.TargetRequests[0].MaxTargets.Should().Be(1);
        exile.IsSorcerySpeed.Should().BeFalse(
            "the exile ability is instant-speed");
    }

    [Fact]
    public void ReturnAbility_HasFourPlusTapPlusSacrificeCost_NoTarget()
    {
        var land = EndlessSandsFactory.Create(_alice);

        var ret = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("4")));

        ret.Costs.OfType<ManaCostCost>().Should().ContainSingle(
            "the {4} generic mana component");
        ret.Costs.OfType<AdditionalCost>().Should().HaveCount(2,
            "{T} tap + Sacrifice this land are both AdditionalCosts");
        ret.TargetRequests.Should().BeEmpty(
            "\"each creature card exiled with this land\" takes no target");
    }

    // -----------------------------------------------------------------------
    // Exile half — records in ledger, moves to exile
    // -----------------------------------------------------------------------

    [Fact]
    public void ExileAbility_Resolution_ExilesTargetCreatureAndLedgersIt()
    {
        var land = EndlessSandsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        var bear = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        bear.SetOwner(_alice);
        bear.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(bear);
        bear.SetZone(ZoneType.Battlefield);

        var exile = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("2")));
        exile.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        foreach (var e in exile.Effects) e.Execute();

        _alice.Zones.Battlefield.ContainsCard(bear).Should().BeFalse(
            "the creature leaves the battlefield");
        _alice.Zones.Exile.ContainsCard(bear).Should().BeTrue(
            "the creature is exiled");
        bear.Zone.Should().Be(ZoneType.Exile);

        var state = EndlessSandsFactory.GetState(land);
        state.Should().NotBeNull();
        state!.ExiledWith.Should().Contain(bear,
            "the exiled creature is recorded in the per-land ledger");
    }

    // -----------------------------------------------------------------------
    // Return half — brings ledgered creatures back under owner's control
    // -----------------------------------------------------------------------

    [Fact]
    public void ReturnAbility_Resolution_ReturnsLedgeredCreaturesUnderOwnerControl()
    {
        var land = EndlessSandsFactory.Create(_alice);
        _alice.Zones.Battlefield.AddCard(land);
        land.SetZone(ZoneType.Battlefield);

        // Bob owns a creature Alice's Endless Sands exiled (e.g. Alice gained
        // control of it). It must return under BOB's control (its owner).
        var bob = new Player("Bob", 20);
        var stolen = new Creature("Stolen Beast", "{2}{G}", 3, 3);
        stolen.SetOwner(bob);
        stolen.SetController(_alice);
        _alice.Zones.Battlefield.AddCard(stolen);
        stolen.SetZone(ZoneType.Battlefield);

        var exile = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("2")));
        exile.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { stolen } });
        foreach (var e in exile.Effects) e.Execute();

        stolen.Zone.Should().Be(ZoneType.Exile, "precondition: it was exiled");

        var ret = land.Abilities.OfType<ActivatedAbility>()
            .Single(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Description.Contains("4")));
        foreach (var e in ret.Effects) e.Execute();

        bob.Zones.Battlefield.ContainsCard(stolen).Should().BeTrue(
            "the creature returns to the battlefield under its OWNER's control");
        stolen.Zone.Should().Be(ZoneType.Battlefield);
        stolen.Controller.Should().BeSameAs(bob,
            "\"under its owner's control\" — owner becomes controller again");

        // The land sacrifices itself as part of the cost-paid resolution.
        _alice.Zones.Graveyard.ContainsCard(land).Should().BeTrue(
            "Endless Sands sacrifices itself");
        land.Zone.Should().Be(ZoneType.Graveyard);

        EndlessSandsFactory.GetState(land)!.ExiledWith.Should().BeEmpty(
            "the ledger is drained as cards return");
    }
}
