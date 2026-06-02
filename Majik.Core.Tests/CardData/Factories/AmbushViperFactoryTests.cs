using System.Linq;
using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.CardData.Factories;
using Majik.Core.Combat;
using Majik.Core.Players;
using Majik.Core.ValueObjects;
using ManaColor = Majik.Core.ValueObjects.ManaColor;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AmbushViperFactory"/>.
///
/// Ambush Viper (Dragons of Tarkir, {1}{G}). Creature — Snake 2/1.
/// Oracle text (verified against Scryfall):
///   "Flash
///    Deathtouch"
///
/// A keyword-only creature — no triggers, no activated abilities. Both
/// keywords are already engine-supported markers:
/// - Flash (CR 702.8) — read by <c>TimingRules</c> to allow instant-speed
///   casting.
/// - Deathtouch (CR 702.2) — read by <c>CombatAbilities.HasDeathtouch</c>
///   for lethal-damage determination.
///
/// Covers:
/// - Identity ({1}{G} Creature — Snake, 2/1, green, mana value 2).
/// - Flash keyword marker (CR 702.8).
/// - Deathtouch keyword marker (CR 702.2) — and the combat helper reports it.
/// - No triggered or non-mana activated abilities (keyword-only creature).
/// </summary>
[Trait("Color", "G")]
public class AmbushViperFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AmbushViper_Identity()
    {
        var c = AmbushViperFactory.Create(_alice);

        c.Name.Should().Be("Ambush Viper");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.BasePower.Should().Be(2);
        c.BaseToughness.Should().Be(1);
        c.HasSubtype(CardSubtype.Snake).Should().BeTrue("Ambush Viper is a Snake");
        c.ManaCost.Should().Be("{1}{G}");
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AmbushViper_IsGreen()
    {
        var c = AmbushViperFactory.Create(_alice);

        // CR 202.2c — color derived from the {G} pip in the mana cost.
        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Green, "Ambush Viper has a {G} pip");
        colors.Should().HaveCount(1, "Ambush Viper is mono-green");
    }

    [Fact]
    public void AmbushViper_ManaValue_IsTwo()
    {
        var c = AmbushViperFactory.Create(_alice);

        // CR 202.3 — {1}{G} = mana value 2.
        c.ManaCostValue.TotalValue.Should().Be(2, "CR 202.3 — {1}{G} has mana value 2");
    }

    [Fact]
    public void AmbushViper_HasFlashKeyword()
    {
        var c = AmbushViperFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Flash",
                "CR 702.8 — Ambush Viper has Flash");
    }

    [Fact]
    public void AmbushViper_HasDeathtouchKeyword()
    {
        var c = AmbushViperFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Should().Contain(k => k.Keyword == "Deathtouch",
                "CR 702.2 — Ambush Viper has Deathtouch");

        // The combat helper reads the marker for lethal-damage determination.
        CombatAbilities.HasDeathtouch(c).Should().BeTrue(
            "CR 702.2 — Deathtouch marker is consumed by CombatAbilities");
    }

    [Fact]
    public void AmbushViper_HasNoTriggeredOrNonManaActivatedAbilities()
    {
        var c = AmbushViperFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>()
            .Should().BeEmpty("Ambush Viper is a keyword-only creature");
        c.Abilities.OfType<ActivatedAbility>()
            .Should().BeEmpty("Ambush Viper has no activated abilities");
    }
}
