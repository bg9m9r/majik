using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="SeethingSongFactory"/>.
///
/// Seething Song (Mirage / Modern Masters, {2}{R}, Instant):
///   "Add {R}{R}{R}{R}{R}."
///
/// Covers:
///   - Card identity (name, instant type, mana cost, owner/controller).
///   - <see cref="NamedCardFactory"/> dispatch by name.
///   - Resolve: adds five red mana to the controller's mana pool.
///   - Resolve is idempotent across multiple casts (mana stacks —
///     CR 106.4).
/// </summary>
public class SeethingSongTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SeethingSong_HasExpectedShape()
    {
        var card = SeethingSongFactory.Create(_alice);

        card.Name.Should().Be("Seething Song");
        card.ManaCost.Should().Be("{2}{R}");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_SeethingSong()
    {
        var card = NamedCardFactory.Create("Seething Song", _alice);

        card.Should().BeOfType<Instant>();
        card.Name.Should().Be("Seething Song");
        card.HasType(CardType.Instant).Should().BeTrue();
        card.ManaCost.Should().Be("{2}{R}");
        card.Owner.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Resolve_AddsFiveRedMana()
    {
        _alice.ManaPool.Total.Should().Be(0);

        var effect = SeethingSongFactory.BuildResolveEffect(_alice).Single();
        effect.Execute();

        _alice.ManaPool.Red.Should().Be(5);
        _alice.ManaPool.White.Should().Be(0);
        _alice.ManaPool.Blue.Should().Be(0);
        _alice.ManaPool.Black.Should().Be(0);
        _alice.ManaPool.Green.Should().Be(0);
        _alice.ManaPool.Generic.Should().Be(0);
        _alice.ManaPool.Total.Should().Be(5);
    }

    [Fact]
    public void Resolve_TwoCopiesInSameStep_StackToTenRed()
    {
        // CR 106.4 — mana from multiple ritual resolutions accumulates in
        // the same pool until the end of the current step/phase.
        var effect1 = SeethingSongFactory.BuildResolveEffect(_alice).Single();
        var effect2 = SeethingSongFactory.BuildResolveEffect(_alice).Single();

        effect1.Execute();
        effect2.Execute();

        _alice.ManaPool.Red.Should().Be(10);
        _alice.ManaPool.Total.Should().Be(10);
    }
}
