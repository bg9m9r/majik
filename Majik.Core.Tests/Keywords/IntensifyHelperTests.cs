using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.Cards;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// Intensity / Intensify (Mystery Booster 2 — Static Discharge) tests.
///
/// <para>Intensity is a card-scoped numeric value tracked on
/// <see cref="Card.Intensity"/> (NOT a permanent counter), so it persists
/// across every zone the card occupies. <see cref="IntensifyHelper"/> models
/// the keyword:</para>
/// <list type="bullet">
///   <item><see cref="IntensifyHelper.Build"/> stamps the printed starting
///   intensity + an "Intensity N" keyword marker.</item>
///   <item><see cref="IntensifyHelper.IntensifyOwnedCopies"/> raises every
///   owned copy (any zone) by N — the printed "cards you own named X
///   intensify by N".</item>
///   <item><see cref="IntensifyHelper.IntensityOf"/> reads the live value off
///   any owned copy (they stay in lock-step).</item>
/// </list>
/// </summary>
public class IntensifyHelperTests
{
    private readonly Player _alice = new("Alice", 20);
    private readonly Player _bob = new("Bob", 20);

    private Sorcery MakeStaticDischarge(Player owner, ZoneType zone)
    {
        var card = new Sorcery("Static Discharge", "{1}{R}")
        {
            Owner = owner,
            Controller = owner,
        };
        card.SetZone(zone);
        owner.Zones.GetZone(zone).AddCard(card);
        return card;
    }

    // -----------------------------------------------------------------------
    // Build — starting intensity + keyword marker
    // -----------------------------------------------------------------------

    [Fact]
    public void Build_StampsStartingIntensity()
    {
        var card = MakeStaticDischarge(_alice, ZoneType.Hand);

        IntensifyHelper.Build(card, startingIntensity: 3);

        card.Intensity.Should().Be(3, "Starting intensity 3");
    }

    [Fact]
    public void Build_StampsIntensityKeywordMarker()
    {
        var card = MakeStaticDischarge(_alice, ZoneType.Hand);

        IntensifyHelper.Build(card, startingIntensity: 3);

        card.Abilities.OfType<KeywordAbility>()
            .Should().ContainSingle(k => k.Keyword == "Intensity 3");
    }

    [Fact]
    public void Build_NegativeStartingIntensity_Throws()
    {
        var card = MakeStaticDischarge(_alice, ZoneType.Hand);

        var act = () => IntensifyHelper.Build(card, startingIntensity: -1);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -----------------------------------------------------------------------
    // Card.Intensify / Card.SetStartingIntensity primitives
    // -----------------------------------------------------------------------

    [Fact]
    public void CardIntensify_RaisesByAmount()
    {
        var card = MakeStaticDischarge(_alice, ZoneType.Graveyard);
        card.SetStartingIntensity(3);

        card.Intensify(1);

        card.Intensity.Should().Be(4);
    }

    [Fact]
    public void CardIntensify_NonPositiveAmount_Throws()
    {
        var card = MakeStaticDischarge(_alice, ZoneType.Hand);

        var act = () => card.Intensify(0);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // -----------------------------------------------------------------------
    // IntensifyOwnedCopies — every owned copy across zones, in lock-step
    // -----------------------------------------------------------------------

    [Fact]
    public void IntensifyOwnedCopies_RaisesEveryOwnedCopyAcrossZones()
    {
        var inGrave = MakeStaticDischarge(_alice, ZoneType.Graveyard);
        var inLibrary = MakeStaticDischarge(_alice, ZoneType.Library);
        var inHand = MakeStaticDischarge(_alice, ZoneType.Hand);
        foreach (var c in new[] { inGrave, inLibrary, inHand })
            c.SetStartingIntensity(3);

        var count = IntensifyHelper.IntensifyOwnedCopies(_alice, "Static Discharge", 1);

        count.Should().Be(3);
        inGrave.Intensity.Should().Be(4);
        inLibrary.Intensity.Should().Be(4);
        inHand.Intensity.Should().Be(4);
    }

    [Fact]
    public void IntensifyOwnedCopies_DoesNotTouchOtherPlayersCopies()
    {
        var mine = MakeStaticDischarge(_alice, ZoneType.Graveyard);
        var theirs = MakeStaticDischarge(_bob, ZoneType.Graveyard);
        mine.SetStartingIntensity(3);
        theirs.SetStartingIntensity(3);

        IntensifyHelper.IntensifyOwnedCopies(_alice, "Static Discharge", 1);

        mine.Intensity.Should().Be(4);
        theirs.Intensity.Should().Be(3, "only cards YOU own intensify");
    }

    [Fact]
    public void IntensifyOwnedCopies_IgnoresDifferentlyNamedCards()
    {
        var sd = MakeStaticDischarge(_alice, ZoneType.Graveyard);
        sd.SetStartingIntensity(3);
        var other = new Sorcery("Lightning Bolt", "{R}") { Owner = _alice, Controller = _alice };
        other.SetZone(ZoneType.Graveyard);
        _alice.Zones.Graveyard.AddCard(other);

        var count = IntensifyHelper.IntensifyOwnedCopies(_alice, "Static Discharge", 1);

        count.Should().Be(1);
        sd.Intensity.Should().Be(4);
    }

    // -----------------------------------------------------------------------
    // IntensityOf — reads the live value off any owned copy
    // -----------------------------------------------------------------------

    [Fact]
    public void IntensityOf_ReadsLiveValue()
    {
        var onStack = MakeStaticDischarge(_alice, ZoneType.Stack);
        onStack.SetStartingIntensity(3);
        onStack.Intensify(2); // now 5

        IntensifyHelper.IntensityOf(_alice, "Static Discharge").Should().Be(5);
    }

    [Fact]
    public void IntensityOf_NoCopy_ReturnsZero()
    {
        IntensifyHelper.IntensityOf(_alice, "Static Discharge").Should().Be(0);
    }
}
