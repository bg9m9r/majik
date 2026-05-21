using FluentAssertions;
using Majik.Core.Abilities;
using Majik.Core.CardData;
using Majik.Core.CardData.Database;
using Majik.Core.Cards;
using Majik.Core.Players;
using Majik.Core.Zones;
using Xunit;

public class OracleLoyaltyAbilityBinderTests
{
    // ---------------------------------------------------------------
    // loyalty-change parsing
    // ---------------------------------------------------------------

    [Fact]
    public void Bind_ParsesThreeAbilities_WithCorrectLoyaltyChanges()
    {
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Test PW", "{3}", startingLoyalty: 4);
        var entity = new CardEntity
        {
            Name = "Test PW",
            TypeLine = "Planeswalker",
            OracleText = "+1: Draw a card.\n-3: You gain 3 life.\n-7: Each opponent loses 10 life.",
        };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);

        var loyalties = pw.Abilities.OfType<LoyaltyAbility>().ToList();
        loyalties.Should().HaveCount(3);
        loyalties[0].LoyaltyChange.Should().Be(1);
        loyalties[1].LoyaltyChange.Should().Be(-3);
        loyalties[2].LoyaltyChange.Should().Be(-7);
    }

    [Fact]
    public void Bind_ZeroAbility_LoyaltyChangeIsZero()
    {
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Lifegainer", "{3}", startingLoyalty: 4);
        var entity = new CardEntity
        {
            Name = "Lifegainer",
            TypeLine = "Planeswalker",
            OracleText = "0: You gain 5 life.",
        };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);

        var ability = pw.Abilities.OfType<LoyaltyAbility>().Single();
        ability.LoyaltyChange.Should().Be(0);
    }

    [Fact]
    public void Bind_UnicodeMinus_ParsedAsNegative()
    {
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Test PW", "{3}", startingLoyalty: 4);
        var entity = new CardEntity
        {
            Name = "Test PW",
            TypeLine = "Planeswalker",
            OracleText = "−2: Draw a card.",  // U+2212 MINUS SIGN
        };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);

        pw.Abilities.OfType<LoyaltyAbility>().Single().LoyaltyChange.Should().Be(-2);
    }

    // ---------------------------------------------------------------
    // draw effect
    // ---------------------------------------------------------------

    [Fact]
    public void Bind_DrawCardsEffect_MovesCardsToHandOnActivation()
    {
        var alice = new Player("Alice", 20);
        alice.Zones.Library.AddCard(new Card("Top"));
        alice.Zones.Library.AddCard(new Card("Next"));
        // zone is Library by default in Card ctor; no need to call SetZone

        var pw = new Planeswalker("Card Drawer", "{2}", startingLoyalty: 3)
        { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var entity = new CardEntity
        {
            Name = "Card Drawer",
            TypeLine = "Planeswalker",
            OracleText = "+1: Draw 2 cards.",
        };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);

        var ability = pw.Abilities.OfType<LoyaltyAbility>().Single();
        ability.Activate();

        alice.Zones.Hand.GetCards().Select(c => c.Name)
            .Should().BeEquivalentTo(new[] { "Top", "Next" });
        pw.Loyalty.Should().Be(4);  // 3 + 1
    }

    [Fact]
    public void Bind_DrawACard_DrawsOne()
    {
        var alice = new Player("Alice", 20);
        alice.Zones.Library.AddCard(new Card("Solo"));

        var pw = new Planeswalker("Drawer", "{1}", startingLoyalty: 3)
        { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var entity = new CardEntity
        {
            Name = "Drawer",
            TypeLine = "Planeswalker",
            OracleText = "+1: Draw a card.",
        };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);
        pw.Abilities.OfType<LoyaltyAbility>().Single().Activate();

        alice.Zones.Hand.GetCards().Should().HaveCount(1);
        alice.Zones.Library.GetCards().Should().BeEmpty();
    }

    // ---------------------------------------------------------------
    // gain-life effect
    // ---------------------------------------------------------------

    [Fact]
    public void Bind_GainLifeEffect_IncreasesControllerLifeTotal()
    {
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Lifegainer", "{3}", startingLoyalty: 4)
        { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var entity = new CardEntity
        {
            Name = "Lifegainer",
            TypeLine = "Planeswalker",
            OracleText = "0: You gain 5 life.",
        };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);
        pw.Abilities.OfType<LoyaltyAbility>().Single().Activate();

        alice.LifeTotal.Should().Be(25);
    }

    // ---------------------------------------------------------------
    // unknown / no-op effects still apply loyalty change
    // ---------------------------------------------------------------

    [Fact]
    public void Bind_UnknownEffectText_NoOpBodyButLoyaltyChangeApplies()
    {
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Mystery Walker", "{5}", startingLoyalty: 8)
        { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var entity = new CardEntity
        {
            Name = "Mystery Walker",
            TypeLine = "Planeswalker",
            OracleText = "+2: Flip a coin. If you win the flip, something complex happens.",
        };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);

        var ability = pw.Abilities.OfType<LoyaltyAbility>().Single();
        ability.LoyaltyChange.Should().Be(2);
        ability.Activate();  // must not throw
        pw.Loyalty.Should().Be(10);  // 8 + 2
    }

    [Fact]
    public void Bind_EachOpponentLosesLife_LoyaltyChangeAppliesWithoutThrowing()
    {
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Oppressor", "{3}", startingLoyalty: 4)
        { Owner = alice, Controller = alice, Zone = ZoneType.Battlefield };
        var entity = new CardEntity
        {
            Name = "Oppressor",
            TypeLine = "Planeswalker",
            OracleText = "-3: Each opponent loses 3 life.",
        };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);

        var ability = pw.Abilities.OfType<LoyaltyAbility>().Single();
        ability.LoyaltyChange.Should().Be(-3);
        ability.Activate();  // no-op body; must not throw
        pw.Loyalty.Should().Be(1);  // 4 - 3
    }

    // ---------------------------------------------------------------
    // non-planeswalker is ignored
    // ---------------------------------------------------------------

    [Fact]
    public void Bind_NonPlaneswalker_AttachesNoAbilities()
    {
        var alice = new Player("Alice", 20);
        var creature = new Creature("Bear", "{1}{G}", 2, 2);
        var entity = new CardEntity
        {
            Name = "Bear",
            TypeLine = "Creature",
            OracleText = "+1: Draw a card.",
        };

        OracleLoyaltyAbilityBinder.Bind(creature, entity, alice);

        creature.Abilities.OfType<LoyaltyAbility>().Should().BeEmpty();
    }

    [Fact]
    public void Bind_NullOracleText_NoAbilitiesAttached()
    {
        var alice = new Player("Alice", 20);
        var pw = new Planeswalker("Silent Walker", "{3}", startingLoyalty: 4);
        var entity = new CardEntity { Name = "Silent Walker", TypeLine = "Planeswalker", OracleText = null };

        OracleLoyaltyAbilityBinder.Bind(pw, entity, alice);

        pw.Abilities.OfType<LoyaltyAbility>().Should().BeEmpty();
    }
}
