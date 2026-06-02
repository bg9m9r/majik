using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="TriskelionFactory"/>.
///
/// Triskelion (Antiquities, {6}) is an Artifact Creature — Construct 1/1.
/// Oracle text:
///   "This creature enters with three +1/+1 counters on it.
///    Remove a +1/+1 counter from this creature: It deals 1 damage to any target."
///
/// The enters-with-counters clause is modelled as an <c>etb_self</c> triggered
/// ability that puts three +1/+1 counters on Triskelion (CR 614.1d describes the
/// printed clause as a replacement; the engine's etb_self trigger reaches the
/// same observable battlefield state — three counters present after it enters).
/// The ping is a remove-+1/+1-counter activated ability dealing 1 damage to any
/// target (CR 115.3 / 306.7 / 608.2b), identical in shape to Walking Ballista's.
/// </summary>
public class TriskelionTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Card identity / types
    // -----------------------------------------------------------------------

    [Fact]
    public void Triskelion_IsCreatureAndArtifact()
    {
        var trike = TriskelionFactory.Create(_alice);

        trike.HasType(CardType.Creature).Should().BeTrue("Triskelion is a Creature");
        trike.HasType(CardType.Artifact).Should().BeTrue("Triskelion is an Artifact");
    }

    [Fact]
    public void Triskelion_HasConstructSubtype()
    {
        var trike = TriskelionFactory.Create(_alice);

        trike.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    [Fact]
    public void Triskelion_IsOneOne()
    {
        var trike = TriskelionFactory.Create(_alice);

        trike.BasePower.Should().Be(1);
        trike.BaseToughness.Should().Be(1);
    }

    [Fact]
    public void Triskelion_OwnerAndControllerAreSet()
    {
        var trike = TriskelionFactory.Create(_alice);

        trike.Owner.Should().BeSameAs(_alice);
        trike.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Ability shape: one ETB trigger + one activated ping
    // -----------------------------------------------------------------------

    [Fact]
    public void Triskelion_HasOneTriggeredAndOneActivatedAbility()
    {
        var trike = TriskelionFactory.Create(_alice);

        trike.Abilities.OfType<TriggeredAbility>().Should().HaveCount(1);
        trike.Abilities.OfType<ActivatedAbility>().Should().HaveCount(1);
    }

    // -----------------------------------------------------------------------
    // Enters with three +1/+1 counters (etb_self -> put_counter 3)
    // -----------------------------------------------------------------------

    [Fact]
    public void Triskelion_EtbTrigger_AddsThreePlusOnePlusOneCounters_OnResolve()
    {
        var trike = TriskelionFactory.Create(_alice);

        var etb = trike.Abilities.OfType<TriggeredAbility>().Single();
        etb.Resolve();

        trike.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(3);
    }

    // -----------------------------------------------------------------------
    // Ping ability: Remove a +1/+1 counter: deal 1 damage to any target
    // -----------------------------------------------------------------------

    [Fact]
    public void Triskelion_PingAbility_HasRemoveCounterCost()
    {
        var trike = TriskelionFactory.Create(_alice);

        var ping = trike.Abilities.OfType<ActivatedAbility>()
            .FirstOrDefault(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());

        ping.Should().NotBeNull("the ping ability should exist with a counter-removal cost");
    }

    [Fact]
    public void Triskelion_PingAbility_CannotPayWhenNoCounters()
    {
        var trike = TriskelionFactory.Create(_alice);
        // starts at 0 counters (ETB trigger not yet resolved)

        var ping = trike.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.CanPay(_alice).Should().BeFalse("no counters to remove");
    }

    [Fact]
    public void Triskelion_PingAbility_RemovesOneCounterOnPay()
    {
        var trike = TriskelionFactory.Create(_alice);
        trike.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var ping = trike.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.Pay(_alice);

        trike.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    [Fact]
    public void Triskelion_PingAbility_ThrowsWhenCounterMissing()
    {
        var trike = TriskelionFactory.Create(_alice);
        // no counters

        var ping = trike.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        var act = () => cost.Pay(_alice);
        act.Should().Throw<InvalidOperationException>();
    }
}
