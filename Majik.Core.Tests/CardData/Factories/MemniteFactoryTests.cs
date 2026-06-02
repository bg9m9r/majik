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
/// Unit tests for <see cref="MemniteFactory"/>.
///
/// Card: Memnite — Artifact Creature — Construct {0} 1/1 (Scars of
/// Mirrodin). Vanilla.
/// </summary>
[Trait("Color", "C")]
public class MemniteFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Memnite_Identity()
    {
        var c = MemniteFactory.Create(_alice);

        c.Name.Should().Be("Memnite");
        c.ManaCost.Should().Be("{0}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasType(CardType.Artifact).Should().BeTrue();
        c.HasSubtype(CardSubtype.Construct).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }
    [Fact]
    public void Memnite_IsVanilla_NoAbilities()
    {
        var c = MemniteFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Memnite is vanilla — no printed keywords");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Memnite has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Memnite has no activated abilities");
    }
}
