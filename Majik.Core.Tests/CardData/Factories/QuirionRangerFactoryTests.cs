using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="QuirionRangerFactory"/>.
///
/// Quirion Ranger (Visions, {G}). Creature — Elf Ranger 1/1. Oracle text:
///   "Return a Forest you control to its owner's hand: Untap target creature.
///    Activate only once each turn."
///
/// Covers:
/// - Identity (name, mana cost, Elf + Ranger subtypes, 1/1, owner/controller).
/// - NamedCardFactory dispatch.
/// - Activated ability present with the return-Forest cost and a 1..1
///   "target creature" target request.
/// - The cost is illegal with no Forest you control; legal with one.
/// - Paying the cost returns the chosen Forest to its owner's hand
///   (CR 118 — paying a cost; CR 701.10 — return to hand).
/// - Resolution untaps the chosen target creature (CR 701.27); idempotent on
///   an already-untapped creature.
/// - "Activate only once each turn" (CR 602.5e) — the cost is illegal a
///   second time within the same turn until the per-turn lock is reset.
/// </summary>
public class QuirionRangerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private static Land MakeForest(Player owner)
    {
        var f = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        f.SetOwner(owner);
        f.SetController(owner);
        f.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(f);
        return f;
    }

    private static Creature MakeBear(Player owner)
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", 2, 2);
        c.SetOwner(owner);
        c.SetController(owner);
        c.SetZone(ZoneType.Battlefield);
        return c;
    }

    private static ActivatedAbility GetUntapAbility(Creature c) =>
        c.Abilities.OfType<ActivatedAbility>().Single();

    [Fact]
    public void QuirionRanger_Identity()
    {
        var c = QuirionRangerFactory.Create(_alice);

        c.Name.Should().Be("Quirion Ranger");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeTrue();
        c.HasSubtype(CardSubtype.Ranger).Should().BeTrue();
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void QuirionRanger_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Quirion Ranger", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Quirion Ranger");
        ((Creature)c).HasSubtype(CardSubtype.Elf).Should().BeTrue();
        ((Creature)c).HasSubtype(CardSubtype.Ranger).Should().BeTrue();
    }

    [Fact]
    public void QuirionRanger_HasActivatedAbility_WithTargetRequest()
    {
        var c = QuirionRangerFactory.Create(_alice);

        var act = c.Abilities.OfType<ActivatedAbility>().ToList();
        act.Should().HaveCount(1, "Quirion Ranger has one activated ability.");

        var ability = act[0];
        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.Costs.Should().HaveCount(1, "the cost is 'Return a Forest you control to its owner's hand'.");
    }

    [Fact]
    public void QuirionRanger_Cost_IllegalWithNoForestYouControl()
    {
        var ranger = QuirionRangerFactory.Create(_alice);
        ranger.SetZone(ZoneType.Battlefield);

        var cost = GetUntapAbility(ranger).Costs[0];
        cost.CanPay(_alice).Should().BeFalse(
            "no Forest you control means the return-a-Forest cost cannot be paid (CR 118).");
    }

    [Fact]
    public void QuirionRanger_Cost_LegalWithForest_ReturnsItToOwnersHand()
    {
        var ranger = QuirionRangerFactory.Create(_alice);
        ranger.SetZone(ZoneType.Battlefield);

        var forest = MakeForest(_alice);

        var cost = GetUntapAbility(ranger).Costs[0];
        cost.CanPay(_alice).Should().BeTrue("Alice controls a Forest.");

        cost.Pay(_alice);

        forest.Zone.Should().Be(ZoneType.Hand,
            "CR 701.10 — paying the cost returns the Forest to its owner's hand.");
        _alice.Zones.Hand.GetCards().Should().Contain(forest);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(forest);
    }

    [Fact]
    public void QuirionRanger_UntapsTargetCreature_AtResolution()
    {
        var ranger = QuirionRangerFactory.Create(_alice);
        ranger.SetZone(ZoneType.Battlefield);

        var bear = MakeBear(_alice);
        bear.Tap();
        bear.IsTapped.Should().BeTrue("bear starts tapped — Quirion Ranger is about to untap it.");

        var ability = GetUntapAbility(ranger);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        foreach (var e in ability.Effects) e.Execute();

        bear.IsTapped.Should().BeFalse(
            "CR 701.27 — Untap target creature leaves the targeted creature untapped.");
    }

    [Fact]
    public void QuirionRanger_AlreadyUntappedTarget_IsNoOp()
    {
        var ranger = QuirionRangerFactory.Create(_alice);
        ranger.SetZone(ZoneType.Battlefield);

        var bear = MakeBear(_alice);
        bear.IsTapped.Should().BeFalse();

        var ability = GetUntapAbility(ranger);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        var act = () => { foreach (var e in ability.Effects) e.Execute(); };
        act.Should().NotThrow("CR 701.27 — untapping an already-untapped creature is a no-op.");
        bear.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void QuirionRanger_NonCreatureTarget_IsResolveTimeNoOp()
    {
        var ranger = QuirionRangerFactory.Create(_alice);
        ranger.SetZone(ZoneType.Battlefield);

        var forest = MakeForest(_alice);
        forest.Tap();

        var ability = GetUntapAbility(ranger);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { forest } });

        foreach (var e in ability.Effects) e.Execute();

        forest.IsTapped.Should().BeTrue(
            "CR 608.2b — a non-creature target is illegal on resolution; the untap is a no-op.");
    }

    [Fact]
    public void QuirionRanger_OncePerTurn_SecondActivationCostIsIllegal()
    {
        var ranger = QuirionRangerFactory.Create(_alice);
        ranger.SetZone(ZoneType.Battlefield);

        MakeForest(_alice);
        MakeForest(_alice);

        var cost = GetUntapAbility(ranger).Costs[0];
        cost.CanPay(_alice).Should().BeTrue("first activation this turn is legal.");
        cost.Pay(_alice);

        cost.CanPay(_alice).Should().BeFalse(
            "CR 602.5e — 'Activate only once each turn' blocks a second activation this turn, " +
            "even though a second Forest is still available.");
    }
}
