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
/// Tests for Aven Mindcensor (Future Sight, {2}{W}).
///
/// Covers:
///   - Card shape: name, type, Bird + Wizard subtypes, P/T 2/1, mana cost,
///     owner / controller wiring.
///   - Flash + Flying keyword markers.
///   - NamedCardFactory dispatch routes the card name to this factory.
///
/// The library-search replacement is a documented v1 gap (no unified
/// library-search interception surface yet — same wall Leonin Arbiter
/// hits). No tests assert search semantics until the primitive lands.
/// </summary>
[Trait("Color", "W")]
public class AvenMindcensorFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AvenMindcensor_IsCreature_BirdWizard_2_1_AtCost2W()
    {
        var c = AvenMindcensorFactory.Create(_alice);

        c.Name.Should().Be("Aven Mindcensor");
        c.ManaCost.Should().Be("{2}{W}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Bird).Should().BeTrue();
        c.HasSubtype(CardSubtype.Wizard).Should().BeTrue();
        c.Power.Should().Be(2);
        c.Toughness.Should().Be(1);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AvenMindcensor_HasFlashAndFlying()
    {
        var c = AvenMindcensorFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flash");
        keywords.Should().Contain("Flying");
    }
}
