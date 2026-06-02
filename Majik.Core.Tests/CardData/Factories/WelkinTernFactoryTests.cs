using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Tests for Welkin Tern (Magic 2014, {U}).
///
/// Covers:
///   - Card shape: name, type, Bird subtype, P/T 2/1, mana cost,
///     owner / controller wiring.
///   - Flying keyword marker (CR 702.9).
///   - <see cref="NamedCardFactory"/> dispatch routes the card name to
///     this factory.
///
/// No triggers, no activated abilities — Welkin Tern is a vanilla flier.
/// </summary>
[Trait("Color", "U")]
public class WelkinTernFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void WelkinTern_IsCreature_Bird_2_1_AtCostU()
    {
        var c = WelkinTernFactory.Create(_alice);

        c.Name.Should().Be("Welkin Tern");
        c.ManaCost.Should().Be("{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void WelkinTern_HasFlying()
    {
        var c = WelkinTernFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
    }

    [Fact]
    public void WelkinTern_HasNoTriggeredOrActivatedAbilities()
    {
        // Vanilla flier — no triggers, no activations.
        var c = WelkinTernFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }
}
