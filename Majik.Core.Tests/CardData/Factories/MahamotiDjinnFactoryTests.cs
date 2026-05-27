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
/// Unit tests for <see cref="MahamotiDjinnFactory"/>.
///
/// Card: Mahamoti Djinn — {4}{U}{U} Creature — Djinn 5/6.
///   "Flying"
///
/// A 5/6 evasive blue flier for six mana — Mahamoti Djinn is a classic
/// Alpha rare, one of the most iconic large blue creatures in Magic's
/// history. It is a vanilla Flying body with no triggered or activated
/// abilities beyond the keyword marker.
/// </summary>
public class MahamotiDjinnFactoryTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity
    // -----------------------------------------------------------------------

    [Fact]
    public void MahamotiDjinn_Identity()
    {
        var c = MahamotiDjinnFactory.Create(_alice);

        c.Name.Should().Be("Mahamoti Djinn");
        c.ManaCost.Should().Be("{4}{U}{U}");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Djinn).Should().BeTrue();
        c.Power.Should().Be(5);
        c.Toughness.Should().Be(6);
        c.Owner.Should().BeSameAs(_alice);
        c.Controller.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Color — blue via CardColors.GetColors (CR 105.1)
    // -----------------------------------------------------------------------

    [Fact]
    public void MahamotiDjinn_IsBlue()
    {
        var c = MahamotiDjinnFactory.Create(_alice);

        CardColors.GetColors(c).Should().Contain(ManaColor.Blue,
            "Mahamoti Djinn has {U}{U} pips in its mana cost");
    }

    // -----------------------------------------------------------------------
    // Mana value — {4}{U}{U} = 4 + 1 + 1 = 6 (CR 202.3)
    // -----------------------------------------------------------------------

    [Fact]
    public void MahamotiDjinn_ManaValueIsSix()
    {
        var c = MahamotiDjinnFactory.Create(_alice);

        // {4}{U}{U} → generic 4 + two blue pips = mana value 6 (CR 202.3).
        ManaCost.Parse(c.ManaCost).TotalValue.Should().Be(6);
    }

    // -----------------------------------------------------------------------
    // Flying keyword marker — CR 702.9
    // -----------------------------------------------------------------------

    [Fact]
    public void MahamotiDjinn_HasFlyingKeywordMarker()
    {
        var c = MahamotiDjinnFactory.Create(_alice);

        c.Abilities.OfType<KeywordAbility>()
            .Any(k => k.Keyword == "Flying").Should().BeTrue(
                "Mahamoti Djinn has Flying as a KeywordAbility marker (CR 702.9)");
    }

    // -----------------------------------------------------------------------
    // No other abilities — Mahamoti Djinn is a vanilla flier
    // -----------------------------------------------------------------------

    [Fact]
    public void MahamotiDjinn_NoOtherAbilities()
    {
        var c = MahamotiDjinnFactory.Create(_alice);

        c.Abilities.OfType<TriggeredAbility>().Should().BeEmpty();
        c.Abilities.OfType<ActivatedAbility>().Should().BeEmpty();
        c.Abilities.OfType<KeywordAbility>().Should().HaveCount(1,
            "Flying is the only printed keyword");
    }

    // -----------------------------------------------------------------------
    // Dispatch via NamedCardFactory
    // -----------------------------------------------------------------------

    [Fact]
    public void MahamotiDjinn_DispatchesViaNamedCardFactory()
    {
        var c = NamedCardFactory.Create("Mahamoti Djinn", _alice);

        c.Should().BeOfType<Creature>();
        c.Name.Should().Be("Mahamoti Djinn");
        c.HasType(CardType.Creature).Should().BeTrue();
        c.HasSubtype(CardSubtype.Djinn).Should().BeTrue();
    }
}
