using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;
using Creature = Majik.Core.Cards.Creature;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="SpinedWurmFactory"/>.
///
/// Card: Spined Wurm — Creature — Wurm {4}{G} 5/4 (Magic 2010 / various
/// reprints). Vanilla — no printed keywords, triggers, statics, or
/// activated abilities.
/// </summary>
public class SpinedWurmFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void SpinedWurm_Identity()
    {
        var c = SpinedWurmFactory.Create(_alice);

        c.Name.Should().Be("Spined Wurm");
        c.ManaCost.Should().Be("{4}{G}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
        c.Power.Should().Be(5);
        c.Toughness.Should().Be(4);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void SpinedWurm_ManaValue_IsFive()
    {
        var c = SpinedWurmFactory.Create(_alice);

        c.ManaCost.Should().Be("{4}{G}",
            "mana value 5: four generic plus one Green pip");
    }

    [Fact]
    public void SpinedWurm_Colors_ContainsGreenOnly()
    {
        var c = SpinedWurmFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Spined Wurm costs {4}{G}");
        colors.Should().HaveCount(1, "Spined Wurm is exactly Green");
    }

    [Fact]
    public void SpinedWurm_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Spined Wurm", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Spined Wurm");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wurm).Should().BeTrue();
    }

    [Fact]
    public void SpinedWurm_IsVanilla_NoAbilities()
    {
        var c = SpinedWurmFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Spined Wurm is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Spined Wurm has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Spined Wurm has no activated abilities");
    }
}
