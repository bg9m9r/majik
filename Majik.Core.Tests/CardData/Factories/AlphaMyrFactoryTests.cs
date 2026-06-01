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
/// Unit tests for <see cref="AlphaMyrFactory"/> (Mirrodin, {2}).
///
/// Artifact Creature — Myr 2/1. Oracle text (verified against Scryfall):
/// empty — Alpha Myr is a vanilla artifact creature (no printed keywords,
/// no mana ability, unlike the rest of the Mirrodin Myr cycle).
///
/// Covers:
///   - Identity (name, cost, P/T, dual Artifact + Creature, Myr subtype,
///     owner / controller).
///   - <see cref="NamedCardFactory"/> dispatch.
///   - Vanilla: no keyword / triggered / activated / mana abilities.
/// </summary>
public class AlphaMyrFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AlphaMyr_Identity()
    {
        var c = (Creature)NamedCardFactory.Create("Alpha Myr", _alice);

        c.Name.Should().Be("Alpha Myr");
        c.ManaCost.Should().Be("{2}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue(
            "Artifact Creature — CR 301.1 / 302.1");
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AlphaMyr_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Alpha Myr", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Alpha Myr");
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Myr).Should().BeTrue();
    }

    [Fact]
    public void AlphaMyr_IsVanilla_NoAbilities()
    {
        var c = (Creature)NamedCardFactory.Create("Alpha Myr", _alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Alpha Myr is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<ManaAbility>().Should().BeEmpty(
            "Alpha Myr has no mana ability (unlike the rest of the Myr cycle)");
    }
}
