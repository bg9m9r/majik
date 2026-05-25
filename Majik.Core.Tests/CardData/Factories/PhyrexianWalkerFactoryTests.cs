using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="PhyrexianWalkerFactory"/>.
///
/// Card: Phyrexian Walker — Artifact Creature — Construct {0} 0/3
/// (Homelands). Vanilla.
/// </summary>
public class PhyrexianWalkerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void PhyrexianWalker_Identity()
    {
        var c = PhyrexianWalkerFactory.Create(_alice);

        c.Name.Should().Be("Phyrexian Walker");
        c.ManaCost.Should().Be("{0}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.Power.Should().Be(0);
        c.Toughness.Should().Be(3);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void PhyrexianWalker_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Phyrexian Walker", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Phyrexian Walker");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
    }

    [Fact]
    public void PhyrexianWalker_IsVanilla_NoAbilities()
    {
        var c = PhyrexianWalkerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Phyrexian Walker is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
