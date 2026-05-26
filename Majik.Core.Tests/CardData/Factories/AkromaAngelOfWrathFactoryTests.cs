using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Combat;
using Majik.Core.Players;
using Xunit;

namespace Majik.Core.Tests.CardData.Factories;

/// <summary>
/// Unit tests for <see cref="AkromaAngelOfWrathFactory"/> (Legions).
///
/// Covers:
/// - Identity ({5}{W}{W}{W} Legendary Creature — Angel 6/6).
/// - All five keyword markers: Flying, First Strike, Vigilance, Trample,
///   Haste (CR 702.9 / 702.7 / 702.20 / 702.19 / 702.10).
/// - Two <see cref="ProtectionAbility"/> markers: black + red
///   (CR 702.16).
/// - <see cref="CombatAbilities"/> evergreen lookups light up for all
///   five combat keywords.
/// - <see cref="NamedCardFactory"/> dispatch.
/// </summary>
public class AkromaAngelOfWrathFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void Akroma_Identity_LegendaryAngel66()
    {
        var c = AkromaAngelOfWrathFactory.Create(_alice);

        c.Name.Should().Be("Akroma, Angel of Wrath");
        c.ManaCost.Should().Be("{5}{W}{W}{W}");
        c.Power.Should().Be(6);
        c.Toughness.Should().Be(6);
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void Akroma_HasAllFiveEvergreenKeywords()
    {
        var c = AkromaAngelOfWrathFactory.Create(_alice);

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain("Flying");
        keywords.Should().Contain("First Strike");
        keywords.Should().Contain("Vigilance");
        keywords.Should().Contain("Trample");
        keywords.Should().Contain("Haste");
    }

    [Fact]
    public void Akroma_CombatAbilities_LookupsAllTrue()
    {
        var c = AkromaAngelOfWrathFactory.Create(_alice);

        CombatAbilities.HasFlying(c).Should().BeTrue();
        CombatAbilities.HasFirstStrike(c).Should().BeTrue();
        CombatAbilities.HasVigilance(c).Should().BeTrue();
        CombatAbilities.HasTrample(c).Should().BeTrue();
        CombatAbilities.HasHaste(c).Should().BeTrue();
    }

    [Fact]
    public void Akroma_HasProtectionFromBlackAndRed()
    {
        var c = AkromaAngelOfWrathFactory.Create(_alice);

        var protections = c.Abilities.OfType<ProtectionAbility>()
            .Select(p => p.Quality).ToList();

        protections.Should().HaveCount(2);
        protections.Should().Contain("black");
        protections.Should().Contain("red");
    }

    [Fact]
    public void Akroma_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Akroma, Angel of Wrath", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Akroma, Angel of Wrath");
        c.HasSubtype(CardSubtype.Angel).Should().BeTrue();
        c.HasSupertype(CardSupertype.Legendary).Should().BeTrue();

        var keywords = c.Abilities.OfType<KeywordAbility>()
            .Select(k => k.Keyword).ToList();
        keywords.Should().Contain(new[]
        {
            "Flying", "First Strike", "Vigilance", "Trample", "Haste",
        });

        c.Abilities.OfType<ProtectionAbility>().Should().HaveCount(2);
    }

    [Fact]
    public void Akroma_NullOwner_Throws()
    {
        var act = () => AkromaAngelOfWrathFactory.Create(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
