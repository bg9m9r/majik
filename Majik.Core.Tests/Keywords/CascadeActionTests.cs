using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Keywords;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.Keywords;

/// <summary>
/// CR 702.85 — Cascade. Tests cover the deterministic mechanical side of
/// the keyword: exile-from-top, eligibility predicate (nonland +
/// MV &lt; source MV), random-order bottoming for non-cast cards, and the
/// "you may" predicate.
/// </summary>
public class CascadeActionTests
{
    private readonly Player _alice = new("Alice", 20);

    /// <summary>
    /// Put cards on top of Alice's library in given order (index 0 = top).
    /// </summary>
    private void StackOnTop(params ICard[] cards)
    {
        // Library.AddCard appends — we want index 0 to be top, so add in
        // forward order (first card is added first / sits at top of stack).
        foreach (var card in cards)
        {
            _alice.Zones.Library.AddCard(card);
            card.SetOwner(_alice);
            card.SetZone(ZoneType.Library);
        }
    }

    [Fact]
    public void EmptyLibrary_NoOp_ReturnsEmptyResult()
    {
        // CR 702.85a — "exile cards from the top of your library until …"
        // with nothing to exile, the trigger is a no-op.
        var result = CascadeAction.Cascade(_alice, sourceManaValue: 4);

        result.Exiled.Should().BeEmpty();
        result.Eligible.Should().BeNull();
        result.Bottomed.Should().BeEmpty();
        _alice.Zones.Library.Count.Should().Be(0);
        _alice.Zones.Exile.Count.Should().Be(0);
    }

    [Fact]
    public void LibraryWithMountainThenBear_ExilesBoth_BearEligible_MountainBottomed()
    {
        // CR 702.85a — keep exiling until the first nonland MV<4. Mountain
        // is land (skip eligibility), Grizzly Bears (MV 2) qualifies.
        var mountain = NamedCardFactory.Create("Mountain", _alice);
        var bear = NamedCardFactory.Create("Grizzly Bears", _alice); // {1}{G}, MV 2.
        StackOnTop(mountain, bear);

        var result = CascadeAction.Cascade(_alice, sourceManaValue: 4);

        result.Exiled.Should().HaveCount(2);
        result.Exiled[0].Should().BeSameAs(mountain);
        result.Exiled[1].Should().BeSameAs(bear);
        result.Eligible.Should().BeSameAs(bear);

        // Bear stays in exile (caller will cast it for free).
        bear.Zone.Should().Be(ZoneType.Exile);
        _alice.Zones.Exile.GetCards().Should().Contain(bear);

        // Mountain (non-eligible) is bottomed.
        result.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(mountain);
        mountain.Zone.Should().Be(ZoneType.Library);
        _alice.Zones.Library.GetCards().Should().Contain(mountain);
    }

    [Fact]
    public void SingleNonLandWithMVLessThanSource_Eligible()
    {
        // Synthetic Lava Spike (MV 1), source MV 4 — eligible.
        var spike = new Sorcery("Lava Spike", "{R}");
        spike.SetOwner(_alice);
        StackOnTop(spike);

        var result = CascadeAction.Cascade(_alice, sourceManaValue: 4);

        result.Eligible.Should().BeSameAs(spike);
        result.Exiled.Should().ContainSingle().Which.Should().BeSameAs(spike);
        result.Bottomed.Should().BeEmpty();
        spike.Zone.Should().Be(ZoneType.Exile);
    }

    [Fact]
    public void MoxOpalMV0_EligibleWhenSourceMVIs1()
    {
        // CR 702.85a — strictly less than. MV-0 artifact is eligible vs
        // an MV-1 cascade source (Bloodbraid Elf's worst-case-can-still-hit-rocks
        // edge case for cards like Bridge from Below / Mox Opal).
        var mox = new Artifact("Mox Opal", "{0}");
        mox.SetOwner(_alice);
        var bolt = new Instant("Lightning Bolt", "{R}");
        bolt.SetOwner(_alice);

        // Top = Mox (will exile first, MV 0 < 1 → eligible).
        StackOnTop(mox, bolt);

        var result = CascadeAction.Cascade(_alice, sourceManaValue: 1);

        result.Eligible.Should().BeSameAs(mox);
        result.Exiled.Should().ContainSingle().Which.Should().BeSameAs(mox);
        result.Bottomed.Should().BeEmpty();
        // Bolt was never exiled (loop stopped at mox).
        bolt.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void NoEligibleCard_All4Bottomed_InRandomOrder()
    {
        // CR 702.85a — when the library is exhausted without an eligible
        // card, all exiled cards go to the bottom in random order
        // (CR 702.85b). Here every card is either a land or MV >= source
        // (4), so none qualifies.
        var m1 = NamedCardFactory.Create("Mountain", _alice);
        var m2 = NamedCardFactory.Create("Forest", _alice);
        var heavy1 = new Sorcery("Big Spell A", "{4}");
        var heavy2 = new Sorcery("Big Spell B", "{5}");
        heavy1.SetOwner(_alice);
        heavy2.SetOwner(_alice);

        StackOnTop(m1, heavy1, m2, heavy2);

        // Seeded RNG so the test asserts a deterministic bottom order.
        var result = CascadeAction.Cascade(
            _alice, sourceManaValue: 4, random: new GameRandom(seed: 42));

        result.Eligible.Should().BeNull();
        result.Exiled.Should().HaveCount(4);
        result.Bottomed.Should().HaveCount(4);
        result.Bottomed.Should().BeEquivalentTo(new ICard[] { m1, heavy1, m2, heavy2 });

        // All cards back in library; library count restored.
        _alice.Zones.Library.Count.Should().Be(4);
        _alice.Zones.Exile.Count.Should().Be(0);
        foreach (var c in new ICard[] { m1, heavy1, m2, heavy2 })
        {
            c.Zone.Should().Be(ZoneType.Library);
        }
    }

    [Fact]
    public void WillCastFalse_EligibleCardIsBottomedToo()
    {
        // CR 702.85a — "You MAY cast that spell". If the controller says
        // no, the eligible card is bottomed along with the rest in random
        // order.
        var bear = NamedCardFactory.Create("Grizzly Bears", _alice);
        StackOnTop(bear);

        var result = CascadeAction.Cascade(
            _alice, sourceManaValue: 4, willCast: _ => false);

        // Eligible-found-but-declined → Eligible is null per the result
        // contract ("did the caller keep it for casting?"). The card itself
        // is bottomed.
        result.Eligible.Should().BeNull();
        result.Exiled.Should().ContainSingle().Which.Should().BeSameAs(bear);
        result.Bottomed.Should().ContainSingle().Which.Should().BeSameAs(bear);
        bear.Zone.Should().Be(ZoneType.Library);
    }

    [Fact]
    public void LandsOnly_AllBottomed_NoEligible()
    {
        // Sanity — a library full of lands cascades into nothing.
        var m1 = NamedCardFactory.Create("Mountain", _alice);
        var m2 = NamedCardFactory.Create("Forest", _alice);
        StackOnTop(m1, m2);

        var result = CascadeAction.Cascade(_alice, sourceManaValue: 4);

        result.Eligible.Should().BeNull();
        result.Exiled.Should().HaveCount(2);
        result.Bottomed.Should().HaveCount(2);
        _alice.Zones.Library.Count.Should().Be(2);
    }
}
