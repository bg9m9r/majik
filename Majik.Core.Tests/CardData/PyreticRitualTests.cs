using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="PyreticRitualFactory"/>.
///
/// Pyretic Ritual (Scars of Mirrodin, {1}{R}, Instant):
///   "Add {R}{R}{R}."
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve: adds three red mana to the controller's mana pool.
///   - Resolve is idempotent across multiple casts (mana stacks —
///     CR 106.4).
/// </summary>
public class PyreticRitualTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PyreticRitual_HasExpectedShape()
    {
        var card = PyreticRitualFactory.Create(_alice);

        card.Name.Should().Be("Pyretic Ritual");
        card.ManaCost.Should().Be("{1}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_PyreticRitual()
    {
        var card = NamedCardFactory.Create("Pyretic Ritual", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Pyretic Ritual");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{1}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_AddsThreeRedMana()
    {
        _alice.ManaPool.Total.Should().Be(0);

        var effect = PyreticRitualFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Red.Should().Be(3);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Blue.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(3);
    }

    [Fact]
    public void Resolve_TwoCopiesInSameStep_StackToSixRed()
    {
        // CR 106.4 — mana from multiple ritual resolutions accumulates in
        // the same pool until the end of the current step/phase.
        var effect1 = PyreticRitualFactory.BuildResolveEffect(_alice).Single();
        var effect2 = PyreticRitualFactory.BuildResolveEffect(_alice).Single();

        effect1.Execute();
        effect2.Execute();

        _alice.ManaPool.Red.Should().Be(6);
        _alice.ManaPool.Total.Should().Be(6);
    }
}
