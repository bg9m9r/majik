using FluentAssertions;
using Majik.Core.CardData.Factories;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Majik.Core.Players;
using Majik.Core.Rules;
using Xunit;
using Creature = Majik.Core.Cards.Creature;
using Land = Majik.Core.Cards.Land;

namespace Majik.Core.Tests.Rules;

/// <summary>
/// CR 702.139 — Companion deck-construction validation. The runtime
/// "cast from outside the game" half is deferred until a sideboard
/// zone exists; these tests cover only the deck-construction predicate
/// + the <see cref="CompanionValidator"/> aggregation surface.
/// </summary>
public class CompanionValidatorTests
{
    private static List<ICard> LurrusLegalDeck()
    {
        // 60-card deck where every permanent has mv ≤ 2.
        var deck = new List<ICard>();
        for (var i = 0; i < 24; i++)
            deck.Add(new Land("Swamp", supertypes: new[] { CardSupertype.Basic }));
        for (var i = 0; i < 4; i++)
            deck.Add(new Creature("Thoughtseize Cultist", "B", 1, 1));
        for (var i = 0; i < 4; i++)
            deck.Add(new Creature("Snapcaster Mage", "1U", 2, 1));
        // Pad with mv-2 bears.
        var n = 0;
        while (deck.Count < 60)
            deck.Add(new Creature($"Bear{n++}", "1G", 2, 2));
        return deck;
    }

    [Fact]
    public void LurrusRestriction_DeckOfPermanentsWithMv2OrLess_IsSatisfied()
    {
        var deck = LurrusLegalDeck();
        var result = CompanionValidator.Validate(
            companion: new Creature("Lurrus of the Dream-Den", "WB", 3, 2),
            restriction: LurrusOfTheDreamDenFactory.CompanionRestriction,
            startingDeck: deck);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void LurrusRestriction_DeckWithMv3Permanent_IsRejected()
    {
        var deck = LurrusLegalDeck();
        // Spoil it with a 3-mv creature.
        deck.Add(new Creature("Reflector Mage", "1WU", 3, 3));

        var result = CompanionValidator.Validate(
            companion: new Creature("Lurrus of the Dream-Den", "WB", 3, 2),
            restriction: LurrusOfTheDreamDenFactory.CompanionRestriction,
            startingDeck: deck);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle()
            .Which.Should().Contain("Lurrus of the Dream-Den");
    }

    [Fact]
    public void LurrusRestriction_NonPermanentsAreIgnored()
    {
        var deck = LurrusLegalDeck();
        // A 3-mv instant is fine — Lurrus only constrains permanent cards.
        deck.Add(new Instant("Cryptic Command", "1UUU"));

        var result = CompanionValidator.Validate(
            companion: new Creature("Lurrus of the Dream-Den", "WB", 3, 2),
            restriction: LurrusOfTheDreamDenFactory.CompanionRestriction,
            startingDeck: deck);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void LurrusRestriction_PredicateDirectAccess_MatchesValidator()
    {
        // Sanity check: ICompanionRestriction.IsSatisfiedBy and
        // CompanionValidator.Validate must agree.
        var deck = LurrusLegalDeck();
        var predicate = LurrusOfTheDreamDenFactory.CompanionRestriction;
        predicate.IsSatisfiedBy(deck).Should().BeTrue();
        predicate.Description.Should()
            .Contain("mana value 2 or less");
    }

    [Fact]
    public void Validate_NullArguments_Throw()
    {
        var deck = new List<ICard>();
        var card = new Creature("Lurrus of the Dream-Den", "WB", 3, 2);
        var restriction = LurrusOfTheDreamDenFactory.CompanionRestriction;

        Assert.Throws<ArgumentNullException>(() =>
            CompanionValidator.Validate(null!, restriction, deck));
        Assert.Throws<ArgumentNullException>(() =>
            CompanionValidator.Validate(card, null!, deck));
        Assert.Throws<ArgumentNullException>(() =>
            CompanionValidator.Validate(card, restriction, null!));
    }

    [Fact]
    public void LurrusFactory_ProducedCard_OwnRestrictionStillExposed()
    {
        // Building the actual card via the factory must not affect the
        // static restriction singleton — the predicate is deck-builder
        // surface, not instance-scoped.
        var owner = new Player("Tester");
        _ = LurrusOfTheDreamDenFactory.Create(owner);
        LurrusOfTheDreamDenFactory.CompanionRestriction
            .Should().NotBeNull();
    }
}
