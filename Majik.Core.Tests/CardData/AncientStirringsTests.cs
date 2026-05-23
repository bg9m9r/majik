using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Tests for Ancient Stirrings (Rise of the Eldrazi, {G}).
/// Sorcery — "Look at top five. May reveal a colorless card to hand.
/// Rest to the bottom in a random order."
/// </summary>
public class AncientStirringsTests
{
    private readonly Player _alice = new("Alice", 20);

    [Fact]
    public void AncientStirrings_Identity()
    {
        var c = AncientStirringsFactory.Create(_alice);

        c.Name.Should().Be("Ancient Stirrings");
        c.ManaCost.Should().Be("{G}");
        c.HasType(CardType.Sorcery).Should().BeTrue();
        c.Owner.Should().Be(_alice);
        c.Controller.Should().Be(_alice);
    }

    [Fact]
    public void NamedCardFactory_Dispatches_AncientStirrings()
    {
        var card = NamedCardFactory.Create("Ancient Stirrings", _alice);

        card.Should().BeOfType<Sorcery>();
        card.Name.Should().Be("Ancient Stirrings");
        card.HasType(CardType.Sorcery).Should().BeTrue();
        card.ManaCost.Should().Be("{G}");
    }

    [Fact]
    public void Resolve_LibraryWithColorlessArtifact_PicksIt_RestToBottom()
    {
        // 5 cards on top: a green creature, then a colourless artifact,
        // then three coloured cards. Default selector picks the first
        // colourless — the artifact at index 1.
        var top5 = new List<ICard>
        {
            SeedLibrary(_alice, "Green Bear", "{G}"),
            SeedLibrary(_alice, "Walking Ballista", ""), // colourless
            SeedLibrary(_alice, "Lightning Bolt", "{R}"),
            SeedLibrary(_alice, "Counterspell", "{U}{U}"),
            SeedLibrary(_alice, "Doom Blade", "{1}{B}"),
        };
        var ballista = top5[1];

        var effects = AncientStirringsFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(ballista);
        _alice.Zones.Hand.GetCards().Count().Should().Be(1);

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Count.Should().Be(4);
        lib.Should().NotContain(ballista);
        // The other 4 peeked cards must all be in the library (now bottomed).
        lib.Should().Contain(new[] { top5[0], top5[2], top5[3], top5[4] });
    }

    [Fact]
    public void Resolve_LibraryAllColoured_NoCardToHand_AllFiveBottom()
    {
        var top5 = new List<ICard>
        {
            SeedLibrary(_alice, "Llanowar Elves", "{G}"),
            SeedLibrary(_alice, "Lightning Bolt", "{R}"),
            SeedLibrary(_alice, "Counterspell", "{U}{U}"),
            SeedLibrary(_alice, "Doom Blade", "{1}{B}"),
            SeedLibrary(_alice, "Wrath of God", "{2}{W}{W}"),
        };

        var effects = AncientStirringsFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Count.Should().Be(5);
        lib.Should().Contain(top5);
    }

    [Fact]
    public void Resolve_LibraryWithFewerThanFiveCards_WorksOnWhatsAvailable()
    {
        // 3 cards total: one colourless, two coloured.
        var bauble = SeedLibrary(_alice, "Mishra's Bauble", "");
        var elves  = SeedLibrary(_alice, "Llanowar Elves", "{G}");
        var bolt   = SeedLibrary(_alice, "Lightning Bolt", "{R}");

        var effects = AncientStirringsFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bauble);
        _alice.Zones.Hand.GetCards().Count().Should().Be(1);

        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Count.Should().Be(2);
        lib.Should().Contain(new[] { elves, bolt });
    }

    [Fact]
    public void Resolve_EmptyLibrary_NoOp()
    {
        // No cards: effect must short-circuit without mutating zones.
        var effects = AncientStirringsFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().BeEmpty();
        _alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    [Fact]
    public void DefaultSelector_PicksFirstColourless()
    {
        // Pure selector test — no library involvement.
        var peeked = new List<ICard>
        {
            MakeCard("Green1", "{G}"),
            MakeCard("Bauble", ""),       // first colourless
            MakeCard("Wastes", ""),        // second colourless (should be ignored)
            MakeCard("Bolt", "{R}"),
            MakeCard("Counter", "{U}{U}"),
        };

        var (toHand, toBottom) = AncientStirringsFactory.DefaultStirringsSelector(peeked);

        toHand.Should().HaveCount(1);
        toHand[0].Name.Should().Be("Bauble");
        toBottom.Should().HaveCount(4);
        toBottom.Should().Contain(c => c.Name == "Green1");
        toBottom.Should().Contain(c => c.Name == "Wastes");
        toBottom.Should().Contain(c => c.Name == "Bolt");
        toBottom.Should().Contain(c => c.Name == "Counter");
        toBottom.Should().NotContain(c => c.Name == "Bauble");
    }

    [Fact]
    public void DefaultSelector_NoColourless_ReturnsAllToBottom()
    {
        var peeked = new List<ICard>
        {
            MakeCard("Elves", "{G}"),
            MakeCard("Bolt", "{R}"),
        };

        var (toHand, toBottom) = AncientStirringsFactory.DefaultStirringsSelector(peeked);

        toHand.Should().BeEmpty();
        toBottom.Should().HaveCount(2);
        toBottom.Should().Contain(peeked);
    }

    [Fact]
    public void Resolve_RandomBottomOrder_AllFourNonRevealedReturnToLibrary()
    {
        // Build a fixed lineup with a single colourless at index 0 so we can
        // assert exactly which 4 cards must end up at the bottom regardless
        // of the random shuffle order applied to them.
        var bauble = SeedLibrary(_alice, "Mishra's Bauble", "");
        var a = SeedLibrary(_alice, "A", "{G}");
        var b = SeedLibrary(_alice, "B", "{R}");
        var c = SeedLibrary(_alice, "C", "{U}");
        var d = SeedLibrary(_alice, "D", "{B}");

        var effects = AncientStirringsFactory.BuildResolveEffect(_alice);
        foreach (var e in effects) e.Execute();

        _alice.Zones.Hand.GetCards().Should().Contain(bauble);
        var lib = _alice.Zones.Library.GetCards().ToList();
        lib.Should().BeEquivalentTo(new[] { a, b, c, d });
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private ICard SeedLibrary(Player p, string name, string manaCost)
    {
        var c = new Card(name, manaCost);
        c.SetOwner(p);
        c.SetZone(ZoneType.Library);
        p.Zones.Library.AddCard(c);
        return c;
    }

    private static ICard MakeCard(string name, string manaCost) => new Card(name, manaCost);
}
