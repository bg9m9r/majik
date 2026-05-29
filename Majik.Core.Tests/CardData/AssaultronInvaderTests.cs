using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Costs;
using Majik.Core.Counters;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Assaultron Invader (Fallout / PIP, {X}{X}, Artifact Creature —
/// Construct 0/0).
///
/// Assaultron Invader is a byte-for-byte functional reprint of Walking
/// Ballista — same cost, same type line, same 0/0, identical oracle text
/// ("This creature enters with X +1/+1 counters on it. {4}: Put a +1/+1
/// counter on this creature. Remove a +1/+1 counter from this creature: It
/// deals 1 damage to any target."). ONLY the printed name differs, so it is
/// served by <see cref="WalkingBallistaFactory"/> via a second
/// <c>[CardName]</c> + the <c>Create(owner, cardName)</c> reprint overload.
///
/// Coverage:
///   - Identity (name, cost, types, subtype, P/T) + NamedCardFactory dispatch.
///   - Same two activated abilities as Walking Ballista (grow + ping).
///   - Walking Ballista itself still builds unchanged.
/// </summary>
public class AssaultronInvaderTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity / dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void AssaultronInvader_HasReprintName()
    {
        var card = WalkingBallistaFactory.Create(_alice, "Assaultron Invader");

        card.Name.Should().Be("Assaultron Invader");
    }

    [Fact]
    public void AssaultronInvader_IsArtifactCreatureConstruct_ZeroZero_XX()
    {
        var card = WalkingBallistaFactory.Create(_alice, "Assaultron Invader");

        card.HasType(CardType.Creature).Should().BeTrue("Assaultron Invader is a Creature");
        card.HasType(CardType.Artifact).Should().BeTrue("Assaultron Invader is an Artifact");
        card.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        card.BasePower.Should().Be(0);
        card.BaseToughness.Should().Be(0);
        card.ManaCost.Should().Be("{X}{X}");
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AssaultronInvader()
    {
        var card = NamedCardFactory.Create("Assaultron Invader", _alice);

        card.Should().BeOfType<Creature>();
        card.Name.Should().Be("Assaultron Invader");
        card.HasType(CardType.Creature).Should().BeTrue();
        card.HasType(CardType.Artifact).Should().BeTrue();
        card.ManaCost.Should().Be("{X}{X}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Abilities — identical to Walking Ballista
    // -----------------------------------------------------------------------

    [Fact]
    public void AssaultronInvader_HasExactlyTwoActivatedAbilities()
    {
        var card = WalkingBallistaFactory.Create(_alice, "Assaultron Invader");

        card.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void AssaultronInvader_GrowAbility_AddsOnePlusOnePlusOneCounter_OnResolve()
    {
        var card = WalkingBallistaFactory.Create(_alice, "Assaultron Invader");

        var grow = card.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<ManaCostCost>().Any(c => c.Cost.TotalValue >= 4));

        grow.Resolve();

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(1);
    }

    [Fact]
    public void AssaultronInvader_PingAbility_HasRemoveCounterCost()
    {
        var card = WalkingBallistaFactory.Create(_alice, "Assaultron Invader");

        var ping = card.Abilities.OfType<ActivatedAbility>()
            .FirstOrDefault(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());

        ping.Should().NotBeNull("the ping ability should exist with a counter-removal cost");
    }

    [Fact]
    public void AssaultronInvader_PingAbility_RemovesOneCounterOnPay()
    {
        var card = WalkingBallistaFactory.Create(_alice, "Assaultron Invader");
        card.Counters.Add(CounterType.PlusOnePlusOne, 3);

        var ping = card.Abilities.OfType<ActivatedAbility>()
            .First(a => a.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Any());
        var cost = ping.Costs.OfType<RemovePlusOnePlusOneCounterCost>().Single();

        cost.Pay(_alice);

        card.Counters.Count(CounterType.PlusOnePlusOne).Should().Be(2);
    }

    // -----------------------------------------------------------------------
    // Reprint isolation — Walking Ballista still builds unchanged
    // -----------------------------------------------------------------------

    [Fact]
    public void WalkingBallista_StillBuildsUnchanged_AlongsideReprint()
    {
        // Build the reprint first to prove the shared definition isn't
        // mutated when the renamed copy is produced.
        _ = WalkingBallistaFactory.Create(_alice, "Assaultron Invader");

        var wb = WalkingBallistaFactory.Create(_alice);

        wb.Name.Should().Be("Walking Ballista");
        wb.HasType(CardType.Creature).Should().BeTrue();
        wb.HasType(CardType.Artifact).Should().BeTrue();
        wb.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        wb.ManaCost.Should().Be("{X}{X}");
        wb.Abilities.OfType<ActivatedAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Create_UnknownName_Throws()
    {
        var act = () => WalkingBallistaFactory.Create(_alice, "Not A Ballista");

        act.Should().Throw<ArgumentException>();
    }
}
