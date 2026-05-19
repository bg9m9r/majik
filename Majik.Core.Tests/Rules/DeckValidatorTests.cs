using FluentAssertions;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Rules;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Land = Majik.Core.Cards.Land;

public class DeckValidatorTests
{
    private static List<ICard> ConstructedDeck(int countOfBolt = 4)
    {
        var deck = new List<ICard>();
        for (var i = 0; i < 24; i++) deck.Add(new Land("Mountain",
            supertypes: new[] { CardSupertype.Basic }));
        for (var i = 0; i < countOfBolt; i++)
            deck.Add(new Instant("Lightning Bolt", "R"));
        // pad with Bears
        var n = 0;
        while (deck.Count < 60) deck.Add(new Creature($"Bear{n++}", "1G", 2, 2));
        return deck;
    }

    // ---------- Standard / Constructed (CR 100) ----------

    [Fact]
    public void ConstructedDeck_Of60_WithFourBolts_IsValid()
    {
        var deck = ConstructedDeck(4);
        var r = DeckValidator.ValidateConstructed(deck);
        r.IsValid.Should().BeTrue();
        r.Errors.Should().BeEmpty();
    }

    [Fact]
    public void ConstructedDeck_With59Cards_FailsMinimum()
    {
        var deck = ConstructedDeck(4);
        deck.RemoveAt(0);
        var r = DeckValidator.ValidateConstructed(deck);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("minimum"));
    }

    [Fact]
    public void ConstructedDeck_With5Bolts_FailsFourOf()
    {
        var deck = ConstructedDeck(5);
        var r = DeckValidator.ValidateConstructed(deck);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("Lightning Bolt"));
    }

    [Fact]
    public void BasicLands_AnyCount_Allowed()
    {
        var deck = new List<ICard>();
        for (var i = 0; i < 60; i++) deck.Add(new Land("Mountain", supertypes: new[] { CardSupertype.Basic }));
        var r = DeckValidator.ValidateConstructed(deck);
        r.IsValid.Should().BeTrue();
    }

    // ---------- Commander (CR 903) ----------

    [Fact]
    public void CommanderDeck_Of100Singletons_IsValid()
    {
        var commander = new Creature("Atraxa", "GWUB", 4, 4,
            supertypes: new[] { CardSupertype.Legendary });
        var deck = new List<ICard>();
        for (var i = 0; i < 99; i++) deck.Add(new Creature($"Card{i}", "1G", 1, 1));
        var r = DeckValidator.ValidateCommander(commander, deck);
        r.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CommanderDeck_With99CardsFails()
    {
        var commander = new Creature("Atraxa", "GWUB", 4, 4,
            supertypes: new[] { CardSupertype.Legendary });
        var deck = new List<ICard>();
        for (var i = 0; i < 98; i++) deck.Add(new Creature($"Card{i}", "1G", 1, 1));
        var r = DeckValidator.ValidateCommander(commander, deck);
        r.IsValid.Should().BeFalse();
    }

    [Fact]
    public void CommanderDeck_WithDuplicateNonLand_Fails()
    {
        var commander = new Creature("Atraxa", "GWUB", 4, 4,
            supertypes: new[] { CardSupertype.Legendary });
        var deck = new List<ICard>();
        deck.Add(new Creature("Bear", "1G", 2, 2));
        deck.Add(new Creature("Bear", "1G", 2, 2));
        for (var i = 0; i < 97; i++) deck.Add(new Creature($"X{i}", "1G", 1, 1));
        var r = DeckValidator.ValidateCommander(commander, deck);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("Bear"));
    }

    [Fact]
    public void CommanderDeck_NonLegendaryCommander_Fails()
    {
        var commander = new Creature("Bear", "1G", 2, 2); // not legendary
        var deck = new List<ICard>();
        for (var i = 0; i < 99; i++) deck.Add(new Creature($"Card{i}", "1G", 1, 1));
        var r = DeckValidator.ValidateCommander(commander, deck);
        r.IsValid.Should().BeFalse();
        r.Errors.Should().Contain(e => e.Contains("legendary"));
    }
}
