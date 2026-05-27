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
/// Unit tests for <see cref="RazortoothRatsFactory"/>.
///
/// Card: Razortooth Rats — {2}{B} Creature — Rat 2/1.
///   "Fear (This creature can't be blocked except by artifact creatures
///    and/or black creatures.)"
/// </summary>
public class RazortoothRatsFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void RazortoothRats_Identity()
    {
        var c = RazortoothRatsFactory.Create(_alice);

        c.Name.Should().Be("Razortooth Rats");
        c.ManaCost.Should().Be("{2}{B}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rat).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void RazortoothRats_IsBlack()
    {
        var c = RazortoothRatsFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Black,
            "Razortooth Rats has {B} pip in its mana cost");
    }

    [Fact]
    public void RazortoothRats_ManaValueIsThree()
    {
        var c = RazortoothRatsFactory.Create(_alice);

        // {2}{B} → 2 generic + 1 coloured = mana value 3 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(3);
    }

    [Fact]
    public void RazortoothRats_HasFearKeywordMarker()
    {
        var c = RazortoothRatsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Fear").Should().BeTrue(
                "Razortooth Rats has Fear (CR 702.36)");
    }

    [Fact]
    public void RazortoothRats_HasExactlyOneKeyword()
    {
        var c = RazortoothRatsFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Fear is the only printed keyword");
    }

    [Fact]
    public void RazortoothRats_NoTriggeredOrActivatedAbilities()
    {
        var c = RazortoothRatsFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
    }

    [Fact]
    public void RazortoothRats_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Razortooth Rats", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Razortooth Rats");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Rat).Should().BeTrue();
    }
}
