using FluentAssertions;
using Majik.Core.CardData;
using Majik.Core.Cards;
using Majik.Core.Cards.Types;
using Xunit;

namespace Majik.Core.Tests.CardData;

/// <summary>
/// Unit tests for <see cref="AuraEnchantClauseParser"/> — CR 702.5b
/// "Enchant X" → cast-time target predicate.
/// </summary>
public class AuraEnchantClauseParserTests
{
    // --- Fixtures: one of each printed CardType so the returned
    // predicate's CardType discrimination can be exercised directly. ---

    private static Permanent Creature()
    {
        var c = new Creature("Grizzly Bears", "{1}{G}", power: 2, toughness: 2);
        return c;
    }

    private static Permanent Land()
    {
        var l = new Land(
            "Forest",
            supertypes: new[] { CardSupertype.Basic },
            subtypes: new[] { CardSubtype.Forest });
        return l;
    }

    private static Permanent Artifact()
    {
        return new Artifact("Sol Ring", "{1}");
    }

    private static Permanent Enchantment()
    {
        return new Enchantment("Glorious Anthem", "{1}{W}{W}");
    }

    private static Permanent Planeswalker()
    {
        // Permanent base is enough for HasType discrimination; the concrete
        // Planeswalker ctor is heavier and not needed for predicate tests.
        var pw = new Planeswalker("Jace, the Mind Sculptor", "{2}{U}{U}", startingLoyalty: 3);
        return pw;
    }

    [Fact]
    public void ParseTargetPredicate_EnchantCreature_MatchesCreaturesOnly()
    {
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "Enchant creature\nEnchanted creature gets +1/+1.");

        predicate.Should().NotBeNull();
        predicate!(Creature()).Should().BeTrue();
        predicate(Land()).Should().BeFalse();
        predicate(Artifact()).Should().BeFalse();
    }

    [Fact]
    public void ParseTargetPredicate_EnchantLand_MatchesLandsOnly()
    {
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "Enchant land\nEnchanted land is an Island.");

        predicate.Should().NotBeNull();
        predicate!(Land()).Should().BeTrue();
        predicate(Creature()).Should().BeFalse();
    }

    [Fact]
    public void ParseTargetPredicate_EnchantArtifact_MatchesArtifactsOnly()
    {
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "Enchant artifact\nEnchanted artifact can't attack.");

        predicate.Should().NotBeNull();
        predicate!(Artifact()).Should().BeTrue();
        predicate(Creature()).Should().BeFalse();
    }

    [Fact]
    public void ParseTargetPredicate_EnchantEnchantment_MatchesEnchantmentsOnly()
    {
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "Enchant enchantment\nWhen enchanted enchantment leaves, draw a card.");

        predicate.Should().NotBeNull();
        predicate!(Enchantment()).Should().BeTrue();
        predicate(Land()).Should().BeFalse();
    }

    [Fact]
    public void ParseTargetPredicate_EnchantPlaneswalker_MatchesPlaneswalkersOnly()
    {
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "Enchant planeswalker\nEnchanted planeswalker has hexproof.");

        predicate.Should().NotBeNull();
        predicate!(Planeswalker()).Should().BeTrue();
        predicate(Creature()).Should().BeFalse();
    }

    [Fact]
    public void ParseTargetPredicate_EnchantPermanent_MatchesAnyPermanent()
    {
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "Enchant permanent\nEnchanted permanent can't be sacrificed.");

        predicate.Should().NotBeNull();
        predicate!(Creature()).Should().BeTrue();
        predicate(Land()).Should().BeTrue();
        predicate(Artifact()).Should().BeTrue();
        predicate(Enchantment()).Should().BeTrue();
        predicate(Planeswalker()).Should().BeTrue();
    }

    [Fact]
    public void ParseTargetPredicate_EnchantPlayer_ThrowsNotSupported()
    {
        // CR 303.4a — player-targeting auras (Curse of …) need a wider
        // engine change; the parser surfaces the gap loudly rather than
        // silently returning a no-op predicate.
        var act = () => AuraEnchantClauseParser.ParseTargetPredicate(
            "Enchant player\nAt the beginning of enchanted player's upkeep…");

        act.Should().Throw<NotSupportedException>()
            .WithMessage("*Enchant player*");
    }

    [Fact]
    public void ParseTargetPredicate_NoClause_ReturnsNull()
    {
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "When this Aura enters, draw a card.");

        predicate.Should().BeNull();
    }

    [Fact]
    public void ParseTargetPredicate_UnrecognisedNoun_ReturnsNull()
    {
        // Multi-word restrictions like "Enchant nonbasic land" or
        // "Enchant creature you control" aren't in v1 scope — the parser
        // returns null and the caller falls back to a hand-wired predicate.
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "Enchant nonbasic land\nEnchanted land is an Island.");

        predicate.Should().BeNull();
    }

    [Fact]
    public void ParseTargetPredicate_NullOrEmpty_ReturnsNull()
    {
        AuraEnchantClauseParser.ParseTargetPredicate(null).Should().BeNull();
        AuraEnchantClauseParser.ParseTargetPredicate("").Should().BeNull();
        AuraEnchantClauseParser.ParseTargetPredicate("   ").Should().BeNull();
    }

    [Fact]
    public void ParseTargetPredicate_CaseInsensitive_StillMatches()
    {
        // Defensive — Scryfall canonical text is capitalised "Enchant"
        // but the parser shouldn't care.
        var predicate = AuraEnchantClauseParser.ParseTargetPredicate(
            "ENCHANT CREATURE\n");

        predicate.Should().NotBeNull();
        predicate!(Creature()).Should().BeTrue();
    }
}
