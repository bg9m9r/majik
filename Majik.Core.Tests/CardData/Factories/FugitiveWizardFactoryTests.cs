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
/// Unit tests for <see cref="FugitiveWizardFactory"/>.
///
/// Card: Fugitive Wizard — Creature — Human Wizard {U} 1/1 (Portal / Portal
/// Second Age). Vanilla — no printed keywords, triggers, statics, or activated
/// abilities.
/// </summary>
[Trait("Color", "U")]
public class FugitiveWizardFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void FugitiveWizard_Identity()
    {
        var c = FugitiveWizardFactory.Create(_alice);

        c.Name.Should().Be("Fugitive Wizard");
        c.ManaCost.Should().Be("{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Human).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.Power.Should().Be(1);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void FugitiveWizard_ManaValue_IsOne()
    {
        var c = FugitiveWizardFactory.Create(_alice);

        c.ManaCost.Should().Be("{U}",
            "mana value 1: one Blue pip only (CR 202.3)");
    }

    [Fact]
    public void FugitiveWizard_Colors_ContainsBlueOnly()
    {
        var c = FugitiveWizardFactory.Create(_alice);

        var colors = CardColors.GetColors(c);
        colors.Should().Contain(ManaColor.Blue, "Fugitive Wizard costs {U}");
        colors.Should().HaveCount(1, "Fugitive Wizard is exactly Blue");
    }
    [Fact]
    public void FugitiveWizard_IsVanilla_NoAbilities()
    {
        var c = FugitiveWizardFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>().Should().BeEmpty(
            "Fugitive Wizard is vanilla — no printed keywords (CR 208.1)");
        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty(
            "Fugitive Wizard has no triggered abilities");
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty(
            "Fugitive Wizard has no activated abilities");
    }
}
