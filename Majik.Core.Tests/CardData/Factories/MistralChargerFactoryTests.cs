using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="MistralChargerFactory"/>.
///
/// Card: Mistral Charger — {1}{W} Creature — Pegasus 2/1.
///   "Flying"
/// A pure vanilla white flier — the only printed ability is Flying
/// (CR 702.9). Same shape as <see cref="WindDrakeFactory"/>'s vanilla
/// flier, materialised from the embedded JSON definition.
/// </summary>
[Trait("Color", "W")]
public class MistralChargerFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void MistralCharger_Identity()
    {
        var c = MistralChargerFactory.Create(_alice);

        c.Name.Should().Be("Mistral Charger");
        c.ManaCost.Should().Be("{1}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Pegasus).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void MistralCharger_IsWhite()
    {
        var c = MistralChargerFactory.Create(_alice);

        // {1}{W} → one white pip (CR 202.2c).
        CardColors.GetColors(c).Should().Contain(ManaColor.White,
            "Mistral Charger has a {W} pip in its mana cost");
    }

    [Fact]
    public void MistralCharger_ManaValueIsTwo()
    {
        var c = MistralChargerFactory.Create(_alice);

        // {1}{W} → generic 1 + one coloured pip = mana value 2 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(2);
    }

    [Fact]
    public void MistralCharger_HasFlyingKeywordMarker()
    {
        var c = MistralChargerFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Mistral Charger ships with Flying as a KeywordAbility marker (CR 702.9)");
    }

    [Fact]
    public void MistralCharger_NoOtherAbilities()
    {
        var c = MistralChargerFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }
}
