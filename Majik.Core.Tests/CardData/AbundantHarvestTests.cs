using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Random;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for <see cref="AbundantHarvestFactory"/> — Sorcery {G}.
///
/// Oracle text (verified against Scryfall):
///   "Choose land or nonland. Reveal cards from the top of your library
///    until you reveal a card of the chosen kind. Put that card into your
///    hand and the rest on the bottom of your library in a random order."
///
/// Covers:
/// - Identity (Sorcery, {G}, green, owner/controller), built from the
///   embedded JSON definition + NamedCardFactory dispatch.
/// - Mode 0 (land): reveal-until-land puts the first land into hand; the
///   revealed nonlands ahead of it bottom (CR 701.15 reveal +
///   CR 701.20 random bottom).
/// - Mode 1 (nonland): reveal-until-nonland puts the first nonland into
///   hand; the revealed lands ahead of it bottom.
/// - The matching card is the only card that leaves the library for hand;
///   every other revealed card is bottomed (none left on top mid-pile).
/// - No card of the chosen kind anywhere: the whole library is revealed,
///   nothing goes to hand, everything is bottomed (CR 701.15a clean stop on
///   empty library).
/// - Empty library: no throw, nothing moves.
/// </summary>
public class AbundantHarvestTests
{
    private readonly Player _alice = new("Alice", 20);

    // -----------------------------------------------------------------------
    // Identity + dispatch
    // -----------------------------------------------------------------------

    [Fact]
    public void Identity_NameTypeAndManaCost()
    {
        var card = AbundantHarvestFactory.Create(_alice);

        card.Name.Should().Be("Abundant Harvest");
        card.ManaCost.Should().Be("{G}");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.Owner.Should().BeSameAs(_alice);
        card.Controller.Should().BeSameAs(_alice);
    }

    [Fact]
    public void AbundantHarvest_IsGreen()
    {
        var card = AbundantHarvestFactory.Create(_alice);

        var colors = CardColors.GetColors(card);
        colors.Should().Contain(Majik.Core.ValueObjects.ManaColor.Green,
            "the {G} pip makes it green");
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AbundantHarvest()
    {
        var card = NamedCardFactory.Create("Abundant Harvest", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Abundant Harvest");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{G}");
        card.Owner.Should().BeSameAs(_alice);
    }

    // -----------------------------------------------------------------------
    // Mode 0 — choose land
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveChoice_Land_RevealsUntilLand_PutsItInHand_BottomsRest()
    {
        // Top → bottom: Nonland, Nonland, Land, Land(untouched).
        // Choosing "land" reveals two nonlands then the first land, which
        // goes to hand; the two nonlands bottom. The fourth card is never
        // revealed.
        var n1 = SeedLibrary(_alice, nonland: true, name: "N1");
        var n2 = SeedLibrary(_alice, nonland: true, name: "N2");
        var land1 = SeedLibrary(_alice, nonland: false, name: "L1");
        var land2 = SeedLibrary(_alice, nonland: false, name: "L2");

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 1234));

        var result = AbundantHarvestFactory.ResolveChoice(
            _alice, AbundantHarvestFactory.ChooseLand);

        result.PutInHand.Should().BeSameAs(land1);
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(land1);

        // Three cards revealed (two nonlands + the terminating land); the two
        // nonlands were bottomed; the untouched land2 stays. Library count =
        // 3 (land2 still up top + the two bottomed nonlands).
        result.Revealed.Should().HaveCount(3);
        result.Revealed.Should().Contain(new[] { n1, n2, land1 });
        _alice.Zones.Library.GetCards().Should().HaveCount(3)
            .And.Contain(new[] { land2, n1, n2 });
        _alice.Zones.Library.GetCards().Should().NotContain(land1,
            "the matching land left the library for hand");
    }

    // -----------------------------------------------------------------------
    // Mode 1 — choose nonland
    // -----------------------------------------------------------------------

    [Fact]
    public void ResolveChoice_Nonland_RevealsUntilNonland_PutsItInHand_BottomsRest()
    {
        // Top → bottom: Land, Land, Nonland, Nonland(untouched).
        var l1 = SeedLibrary(_alice, nonland: false, name: "L1");
        var l2 = SeedLibrary(_alice, nonland: false, name: "L2");
        var non1 = SeedLibrary(_alice, nonland: true, name: "N1");
        var non2 = SeedLibrary(_alice, nonland: true, name: "N2");

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 99));

        var result = AbundantHarvestFactory.ResolveChoice(
            _alice, AbundantHarvestFactory.ChooseNonland);

        result.PutInHand.Should().BeSameAs(non1);
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(non1);

        result.Revealed.Should().HaveCount(3);
        result.Revealed.Should().Contain(new[] { l1, l2, non1 });
        _alice.Zones.Library.GetCards().Should().HaveCount(3)
            .And.Contain(new[] { non2, l1, l2 });
        _alice.Zones.Library.GetCards().Should().NotContain(non1);
    }

    [Fact]
    public void ResolveChoice_FirstCardMatches_PutsItInHand_NoOtherReveal()
    {
        // Top card is already a land; choosing land takes it immediately.
        var land = SeedLibrary(_alice, nonland: false, name: "L1");
        var rest = SeedLibrary(_alice, nonland: true, name: "N1");

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 7));

        var result = AbundantHarvestFactory.ResolveChoice(
            _alice, AbundantHarvestFactory.ChooseLand);

        result.PutInHand.Should().BeSameAs(land);
        result.Revealed.Should().ContainSingle().Which.Should().BeSameAs(land);
        _alice.Zones.Hand.GetCards().Should().ContainSingle().Which.Should().BeSameAs(land);
        _alice.Zones.Library.GetCards().Should().ContainSingle().Which.Should().BeSameAs(rest,
            "no card was revealed past the immediate match, so nothing was bottomed");
    }

    [Fact]
    public void ResolveChoice_NoMatchingKind_RevealsAll_NothingToHand_AllBottomed()
    {
        // All-land library, but the player chose nonland: reveal exhausts the
        // library, nothing goes to hand, everything is bottomed.
        var l1 = SeedLibrary(_alice, nonland: false, name: "L1");
        var l2 = SeedLibrary(_alice, nonland: false, name: "L2");
        var l3 = SeedLibrary(_alice, nonland: false, name: "L3");

        GameRandomRegistry.Set(_alice, new GameRandom(seed: 5));

        var result = AbundantHarvestFactory.ResolveChoice(
            _alice, AbundantHarvestFactory.ChooseNonland);

        result.PutInHand.Should().BeNull("no nonland was ever revealed");
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        result.Revealed.Should().HaveCount(3).And.Contain(new[] { l1, l2, l3 });
        _alice.Zones.Library.GetCards().Should().HaveCount(3,
            "every revealed card was bottomed; none reached the hand");
    }

    [Fact]
    public void ResolveChoice_EmptyLibrary_NoThrow_NothingMoves()
    {
        GameRandomRegistry.Set(_alice, new GameRandom(seed: 42));

        var result = AbundantHarvestFactory.ResolveChoice(
            _alice, AbundantHarvestFactory.ChooseLand);

        result.PutInHand.Should().BeNull();
        result.Revealed.Should().BeEmpty();
        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Helpers — index 0 of the library list is the TOP (GetCards order).
    // -----------------------------------------------------------------------

    private static ICard SeedLibrary(Player p, bool nonland, string name)
    {
        ICard c = nonland
            ? new Instant(name, "{1}")
            : new Land(name);
        c.SetOwner(p);
        c.SetController(p);
        p.Zones.Library.AddCard(c);
        c.SetZone(ZoneType.Library);
        return c;
    }
}
