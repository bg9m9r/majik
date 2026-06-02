using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="WirewoodSymbioteFactory"/>.
///
/// Wirewood Symbiote (Legions, {G}). Creature — Insect 1/1. Oracle text:
///   "Return an Elf you control to its owner's hand: Untap target creature.
///    Activate only once each turn."
///
/// Covers:
/// - Identity (name, mana cost, Insect subtype, 1/1, owner/controller).
/// - Activated ability present with the return-Elf cost and a 1..1 "target
///   creature" target request.
/// - The cost is illegal with no Elf you control; legal with one.
/// - Paying the cost returns the chosen Elf to its owner's hand
///   (CR 118 — paying a cost; CR 701.10 — return to hand).
/// - Resolution untaps the chosen target creature (CR 701.27); idempotent on
///   an already-untapped creature; non-creature target is a resolve-time no-op.
/// - "Activate only once each turn" (CR 602.5e) — the cost is illegal a second
///   time within the same turn until the per-turn lock is reset.
/// </summary>
[Trait("Color", "G")]
public class WirewoodSymbioteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    private static Creature MakeElf(Player owner)
    {
        var e = new Creature("Llanowar Elves", "{G}", 1, 1, subtypes: new[] { CardSubtype.Elf });
        e.SetOwner(owner);
        e.SetController(owner);
        e.SetZone(ZoneType.Battlefield);
        owner.Zones.Battlefield.AddCard(e);
        return e;
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
    public void WirewoodSymbiote_Identity()
    {
        var c = WirewoodSymbioteFactory.Create(_alice);

        c.Name.Should().Be("Wirewood Symbiote");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Insect).Should().BeTrue();
        c.HasSubtype(CardSubtype.Elf).Should().BeFalse(
            "Wirewood Symbiote is an Insect, not an Elf — it cannot pay its own cost.");
        c.BasePower.Should().Be(1);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WirewoodSymbiote_HasActivatedAbility_WithTargetRequest()
    {
        var c = WirewoodSymbioteFactory.Create(_alice);

        var act = c.Abilities.OfType<ActivatedAbility>().ToList();
        act.Should().HaveCount(1, "Wirewood Symbiote has one activated ability.");

        var ability = act[0];
        ability.TargetRequests.Should().HaveCount(1);
        ability.TargetRequests[0].MinTargets.Should().Be(1);
        ability.TargetRequests[0].MaxTargets.Should().Be(1);
        ability.Costs.Should().HaveCount(1, "the cost is 'Return an Elf you control to its owner's hand'.");
    }

    [Fact]
    public void WirewoodSymbiote_Cost_IllegalWithNoElfYouControl()
    {
        var symbiote = WirewoodSymbioteFactory.Create(_alice);
        symbiote.SetZone(ZoneType.Battlefield);

        var cost = GetUntapAbility(symbiote).Costs[0];
        cost.CanPay(_alice).Should().BeFalse(
            "no Elf you control means the return-an-Elf cost cannot be paid (CR 118).");
    }

    [Fact]
    public void WirewoodSymbiote_Cost_LegalWithElf_ReturnsItToOwnersHand()
    {
        var symbiote = WirewoodSymbioteFactory.Create(_alice);
        symbiote.SetZone(ZoneType.Battlefield);

        var elf = MakeElf(_alice);

        var cost = GetUntapAbility(symbiote).Costs[0];
        cost.CanPay(_alice).Should().BeTrue("Alice controls an Elf.");

        cost.Pay(_alice);

        elf.Zone.Should().Be(ZoneType.Hand,
            "CR 701.10 — paying the cost returns the Elf to its owner's hand.");
        _alice.Zones.Hand.GetCards().Should().Contain(elf);
        _alice.Zones.Battlefield.GetCards().Should().NotContain(elf);
    }

    [Fact]
    public void WirewoodSymbiote_UntapsTargetCreature_AtResolution()
    {
        var symbiote = WirewoodSymbioteFactory.Create(_alice);
        symbiote.SetZone(ZoneType.Battlefield);

        var bear = MakeBear(_alice);
        bear.Tap();
        bear.IsTapped.Should().BeTrue("bear starts tapped — Wirewood Symbiote is about to untap it.");

        var ability = GetUntapAbility(symbiote);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        foreach (var e in ability.Effects) e.Execute();

        bear.IsTapped.Should().BeFalse(
            "CR 701.27 — Untap target creature leaves the targeted creature untapped.");
    }

    [Fact]
    public void WirewoodSymbiote_AlreadyUntappedTarget_IsNoOp()
    {
        var symbiote = WirewoodSymbioteFactory.Create(_alice);
        symbiote.SetZone(ZoneType.Battlefield);

        var bear = MakeBear(_alice);
        bear.IsTapped.Should().BeFalse();

        var ability = GetUntapAbility(symbiote);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { bear } });

        var act = () => { foreach (var e in ability.Effects) e.Execute(); };
        act.Should().NotThrow("CR 701.27 — untapping an already-untapped creature is a no-op.");
        bear.IsTapped.Should().BeFalse();
    }

    [Fact]
    public void WirewoodSymbiote_NonCreatureTarget_IsResolveTimeNoOp()
    {
        var symbiote = WirewoodSymbioteFactory.Create(_alice);
        symbiote.SetZone(ZoneType.Battlefield);

        var forest = new Land("Forest", subtypes: new[] { CardSubtype.Forest });
        forest.SetOwner(_alice);
        forest.SetController(_alice);
        forest.SetZone(ZoneType.Battlefield);
        forest.Tap();

        var ability = GetUntapAbility(symbiote);
        ability.SetChosenTargets(new IReadOnlyList<object>[] { new object[] { forest } });

        foreach (var e in ability.Effects) e.Execute();

        forest.IsTapped.Should().BeTrue(
            "CR 608.2b — a non-creature target is illegal on resolution; the untap is a no-op.");
    }

    [Fact]
    public void WirewoodSymbiote_OncePerTurn_SecondActivationCostIsIllegal()
    {
        var symbiote = WirewoodSymbioteFactory.Create(_alice);
        symbiote.SetZone(ZoneType.Battlefield);

        MakeElf(_alice);
        MakeElf(_alice);

        var cost = GetUntapAbility(symbiote).Costs[0];
        cost.CanPay(_alice).Should().BeTrue("first activation this turn is legal.");
        cost.Pay(_alice);

        cost.CanPay(_alice).Should().BeFalse(
            "CR 602.5e — 'Activate only once each turn' blocks a second activation this turn, " +
            "even though a second Elf is still available.");
    }
}
